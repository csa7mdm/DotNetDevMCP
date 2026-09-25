// Copyright (c) 2025 Ahmed Mustafa

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DotNetDevMCP.Core;
using DotNetDevMCP.Core.Models;

namespace DotNetDevMCP.Testing;

/// <summary>
/// Runs `dotnet test` once per project/solution and parses the TRX it writes.
/// One process, the framework's own parallelism, structured per-test results.
/// </summary>
public sealed class TestRunner
{
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public async Task<IReadOnlyList<TestCase>> DiscoverAsync(string projectPath, string? filter, CancellationToken ct)
    {
        projectPath = Path.GetFullPath(projectPath);
        var args = new List<string> { "test", projectPath, "--list-tests" };
        args.AddRange(FilterArgs(filter));
        var (exit, stdout, stderr, _) = await RunDotnetAsync(args, ct, DirectoryOf(projectPath));
        if (exit != 0)
        {
            throw new InvalidOperationException($"dotnet test --list-tests failed:\n{Tail(stdout + stderr)}");
        }

        // Output is "The following Tests are available:" followed by one indented name per line.
        var tests = new List<TestCase>();
        var listing = false;
        foreach (var line in stdout.Split('\n'))
        {
            if (line.Contains("The following Tests are available:")) { listing = true; continue; }
            if (!listing) continue;
            if (line.Length == 0 || !char.IsWhiteSpace(line[0])) { listing = false; continue; }
            tests.Add(new TestCase(line.Trim(), projectPath));
        }
        return tests;
    }

    /// <param name="testNames">Fully qualified names; empty runs everything the filter allows.</param>
    /// <param name="framework">Target framework to run (e.g. net10.0); null runs every TFM the projects target.</param>
    /// <param name="timeoutSeconds">A run must always return: injected faults or a real deadlock can hang a test forever, which
    /// would hang the MCP tool call with it. Past this, the whole process tree is killed and a failed summary is returned.</param>
    public async Task<TestRunSummary> RunAsync(string projectOrSolutionPath, string? filter, IReadOnlyCollection<string>? testNames, bool noBuild, string? framework, CancellationToken ct, int timeoutSeconds = 600)
    {
        var frameworkError = DotnetArgumentValidation.ValidateFramework(framework);
        if (frameworkError != null) return TestRunSummary.Failed(frameworkError, TimeSpan.Zero);

        projectOrSolutionPath = Path.GetFullPath(projectOrSolutionPath);
        var mtp = UsesTestingPlatform(projectOrSolutionPath);
        if (mtp && ValidateTestingPlatformFilter(filter) is { } filterError) return TestRunSummary.Failed(filterError, TimeSpan.Zero);
        return mtp
            ? await RunTestingPlatformAsync(projectOrSolutionPath, filter, testNames, noBuild, framework, ct, timeoutSeconds)
            : await RunVsTestAsync(projectOrSolutionPath, filter, testNames, noBuild, framework, ct, timeoutSeconds);
    }

    /// <summary>
    /// .NET 10 `dotnet test` runs in Microsoft.Testing.Platform mode when global.json has "test": { "runner": "Microsoft.Testing.Platform" }.
    /// That mode rejects the VSTest options (--logger, --filter expression, positional path).
    /// </summary>
    public static bool UsesTestingPlatform(string path)
    {
        for (var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var globalJson = Path.Combine(dir, "global.json");
            if (!File.Exists(globalJson)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(globalJson), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                return doc.RootElement.TryGetProperty("test", out var test)
                    && test.TryGetProperty("runner", out var runner)
                    && string.Equals(runner.GetString(), "Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase);
            }
            catch (JsonException) { return false; }
        }
        return false;
    }

    // Report and filter options are framework extensions under MTP: xUnit v3 has --report-xunit-trx / --filter-method,
    // MSTest and NUnit have --report-trx / --filter. Try xUnit first; exit code 5 (invalid command line) means the other.
    // ponytail: one flavor per path; a solution mixing xUnit v3 and MSTest projects under MTP is not handled.
    private static readonly ConcurrentDictionary<string, bool> IsXunitByPath = new(StringComparer.OrdinalIgnoreCase);
    private const int MtpInvalidCommandLine = 5;
    private const int MtpZeroTestsRan = 8;

    private async Task<TestRunSummary> RunTestingPlatformAsync(string path, string? filter, IReadOnlyCollection<string>? testNames, bool noBuild, string? framework, CancellationToken ct, int timeoutSeconds)
    {
        var target = path.EndsWith("proj", StringComparison.OrdinalIgnoreCase) ? "--project"
            : Directory.Exists(path) ? "--directory" : "--solution";
        var sw = Stopwatch.StartNew();
        (int ExitCode, string Stdout, string Stderr, bool TimedOut) run = default;
        foreach (var xunit in IsXunitByPath.TryGetValue(path, out var known) ? new[] { known } : new[] { true, false })
        {
            var args = new List<string> { "test", target, path };
            if (noBuild) args.Add("--no-build");
            if (!string.IsNullOrWhiteSpace(framework)) { args.Add("--framework"); args.Add(framework); }
            args.Add(xunit ? "--report-xunit-trx" : "--report-trx");
            // TestingPlatformNameFilter still returns its quoted string form (tested as such); splitting it back
            // into argv tokens here reproduces exactly what it describes, one ArgumentList entry per token.
            args.AddRange(SplitArgs(TestingPlatformNameFilter(testNames, xunit)));
            if (!string.IsNullOrWhiteSpace(filter)) args.AddRange(SplitArgs(filter)); // the framework's own filter options, verbatim
            run = await RunDotnetAsync(args, ct, DirectoryOf(path), TimeSpan.FromSeconds(timeoutSeconds));
            if (run.ExitCode != MtpInvalidCommandLine) { if (!run.TimedOut) IsXunitByPath[path] = xunit; break; }
            noBuild = true; // the first attempt already built
        }
        sw.Stop();

        if (run.TimedOut)
        {
            return TestRunSummary.Failed(TimeoutMessage(timeoutSeconds, run.Stdout + "\n" + run.Stderr), sw.Elapsed);
        }

        // No --results-directory: repos often set their own through TestingPlatformCommandLineArguments and MTP rejects a second one.
        // Every module prints the files it wrote under "file artifacts produced", one "- <path>" per line.
        var trxFiles = (run.Stdout + "\n" + run.Stderr).Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal) && l.EndsWith(".trx", StringComparison.OrdinalIgnoreCase))
            .Select(l => l[2..].Trim())
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (trxFiles.Length == 0)
        {
            return TestRunSummary.Failed(run.ExitCode == MtpZeroTestsRan ? "Zero tests ran (the filter matched nothing)." : Tail(run.Stdout + run.Stderr), sw.Elapsed);
        }
        return TestRunSummary.Merge(trxFiles.Select(ParseTrx)) with { Duration = sw.Elapsed };
    }

    /// <summary>
    /// Message for a killed run: names the modules Microsoft.Testing.Platform reported starting but never reported finishing
    /// (the likely hang), falling back to the tail of the output if the format doesn't match (different MTP version, VSTest mixed in).
    /// </summary>
    private static string TimeoutMessage(int timeoutSeconds, string output)
    {
        var unfinished = UnfinishedModules(output);
        var detail = unfinished.Count > 0 ? $"Started but never finished: {string.Join(", ", unfinished)}" : Tail(output);
        return $"Test run timed out after {timeoutSeconds}s and likely hangs. {detail}";
    }

    /// <summary>
    /// MTP prints "Running tests from &lt;path&gt;" when a module starts and "&lt;path&gt; (&lt;tfm&gt;|&lt;arch&gt;) passed|failed (...)"
    /// when it ends. A module with a start line and no matching end line is the one still running when the run was killed.
    /// </summary>
    public static IReadOnlyList<string> UnfinishedModules(string output)
    {
        const string StartPrefix = "Running tests from ";
        var started = new List<string>();
        var finished = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith(StartPrefix, StringComparison.Ordinal))
            {
                started.Add(line[StartPrefix.Length..].Trim());
                continue;
            }
            var openParen = line.IndexOf(" (", StringComparison.Ordinal);
            var closeParen = openParen < 0 ? -1 : line.IndexOf(')', openParen);
            if (closeParen < 0 || !line[(openParen + 2)..closeParen].Contains('|')) continue;
            var rest = line[(closeParen + 1)..].TrimStart();
            if (rest.StartsWith("passed", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("failed", StringComparison.OrdinalIgnoreCase))
            {
                finished.Add(line[..openParen].Trim());
            }
        }
        return started.Where(s => !finished.Contains(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Exact-name filter for MTP. Past the Windows command-line limit it widens to classes, then to the whole target (a superset, never fewer tests).</summary>
    public static string TestingPlatformNameFilter(IReadOnlyCollection<string>? names, bool xunit)
    {
        if (names is not { Count: > 0 }) return "";
        string Build(IEnumerable<string> items, string xunitOption, Func<string, string> vstestTerm) => xunit
            ? string.Concat(items.Select(n => $" {xunitOption} \"{n}\""))
            : $" --filter \"{string.Join("|", items.Select(vstestTerm))}\"";

        var byMethod = Build(names, "--filter-method", n => $"FullyQualifiedName={n}");
        if (byMethod.Length <= MaxFilterChars) return byMethod;
        var classes = names.Select(n => n.LastIndexOf('.') is var i and > 0 ? n[..i] : n).Distinct().ToList();
        var byClass = Build(classes, "--filter-class", c => $"FullyQualifiedName~{c}.");
        return byClass.Length <= MaxFilterChars ? byClass : "";
    }

    private const int MaxFilterChars = 24_000; // Windows caps a command line at 32,767 chars

    /// <summary>
    /// Exact-name OR filter for VSTest, widened past <see cref="MaxFilterChars"/> the same way
    /// <see cref="TestingPlatformNameFilter"/> widens for MTP: first to one contains-term per class, then
    /// (still too long) to null, meaning "no name filter" — every widening step is a superset of the exact
    /// names, so a run under the widened filter never executes fewer tests than the caller asked for.
    /// </summary>
    public static string? WidenVsTestFilter(IReadOnlyCollection<string> names)
    {
        var byName = string.Join("|", names.Select(n => $"FullyQualifiedName={n}"));
        if (byName.Length <= MaxFilterChars) return byName;
        var classes = names.Select(n => n.LastIndexOf('.') is var i and > 0 ? n[..i] : n).Distinct().ToList();
        var byClass = string.Join("|", classes.Select(c => $"FullyQualifiedName~{c}."));
        return byClass.Length <= MaxFilterChars ? byClass : null;
    }

    private async Task<TestRunSummary> RunVsTestAsync(string projectOrSolutionPath, string? filter, IReadOnlyCollection<string>? testNames, bool noBuild, string? framework, CancellationToken ct, int timeoutSeconds)
    {
        var resultsDir = Path.Combine(Path.GetTempPath(), "dotnetdevmcp-trx", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsDir);

        var fullFilter = filter;
        if (testNames is { Count: > 0 })
        {
            var widened = WidenVsTestFilter(testNames);
            fullFilter = widened is null
                ? filter // widening gave up entirely: fall back to just the base filter (or none), a superset of the exact names.
                : string.IsNullOrEmpty(filter) ? widened : $"({filter})&({widened})";
        }

        var args = new List<string> { "test", projectOrSolutionPath, "--logger", "trx", "--results-directory", resultsDir };
        if (noBuild) args.Add("--no-build");
        if (!string.IsNullOrWhiteSpace(framework)) { args.Add("--framework"); args.Add(framework); }
        // Named per-test, not per-run: the process timeout below already bounds the whole run. Naming the hanging test needs a
        // shorter per-test window, capped at the run timeout so it can never itself become the reason nothing finishes in time.
        var hangTimeout = Math.Min(120, timeoutSeconds);
        args.Add("--blame-hang"); args.Add("--blame-hang-timeout"); args.Add($"{hangTimeout}s"); args.Add("--blame-hang-dump-type"); args.Add("none"); // the name, not a multi-GB dump
        args.AddRange(FilterArgs(fullFilter));

        var sw = Stopwatch.StartNew();
        var run = await RunDotnetAsync(args, ct, DirectoryOf(projectOrSolutionPath), TimeSpan.FromSeconds(timeoutSeconds));
        sw.Stop();

        if (run.TimedOut)
        {
            // Blame should have already killed and reported the hanging test before this fires; if not, the whole tree is gone now.
            return TestRunSummary.Failed($"Test run timed out after {timeoutSeconds}s and likely hangs.\n{Tail(run.Stdout + run.Stderr)}", sw.Elapsed);
        }

        var trxFiles = Directory.GetFiles(resultsDir, "*.trx", SearchOption.AllDirectories);
        if (trxFiles.Length == 0)
        {
            // Build failure or bad path: exit != 0 and nothing written. "No test is available" also lands here.
            return TestRunSummary.Failed(run.ExitCode == 0 ? "dotnet test ran but wrote no results (no tests matched?)" : Tail(run.Stdout + run.Stderr), sw.Elapsed);
        }

        var summary = TestRunSummary.Merge(trxFiles.Select(ParseTrx));
        try { Directory.Delete(resultsDir, recursive: true); } catch { /* temp dir, best effort */ }
        return summary with { Duration = sw.Elapsed };
    }

    public static TestRunSummary ParseTrx(string path) => ParseTrx(XDocument.Load(path));

    public static TestRunSummary ParseTrx(XDocument doc)
    {
        // TestDefinitions/UnitTest gives class+method; Results/UnitTestResult gives outcome per test id.
        var definitions = doc.Descendants(Trx + "UnitTest")
            .Select(u => (Id: (string?)u.Attribute("id"), Method: u.Element(Trx + "TestMethod")))
            .Where(d => d.Id is not null && d.Method is not null)
            .ToDictionary(d => d.Id!, d => $"{(string?)d.Method!.Attribute("className")}.{(string?)d.Method.Attribute("name")}");

        var results = new List<TestResult>();
        foreach (var r in doc.Descendants(Trx + "UnitTestResult"))
        {
            var id = (string?)r.Attribute("testId") ?? "";
            var display = (string?)r.Attribute("testName") ?? id;
            var outcome = ((string?)r.Attribute("outcome") ?? "") switch
            {
                "Passed" => TestOutcome.Passed,
                "Failed" => TestOutcome.Failed,
                "NotExecuted" or "Inconclusive" => TestOutcome.Skipped,
                _ => TestOutcome.NotRun,
            };
            var duration = TimeSpan.TryParse((string?)r.Attribute("duration"), out var d) ? d : TimeSpan.Zero;
            var output = r.Element(Trx + "Output");
            var errorInfo = output?.Element(Trx + "ErrorInfo");
            results.Add(new TestResult(
                definitions.GetValueOrDefault(id, display),
                display,
                outcome,
                duration,
                (string?)errorInfo?.Element(Trx + "Message"),
                (string?)errorInfo?.Element(Trx + "StackTrace"),
                (string?)output?.Element(Trx + "StdOut")));
        }

        return new TestRunSummary(
            results.Count,
            results.Count(x => x.Outcome == TestOutcome.Passed),
            results.Count(x => x.Outcome == TestOutcome.Failed),
            results.Count(x => x.Outcome == TestOutcome.Skipped),
            TimeSpan.FromTicks(results.Sum(x => x.Duration.Ticks)),
            results);
    }

    private static IEnumerable<string> FilterArgs(string? filter) =>
        string.IsNullOrWhiteSpace(filter) ? [] : ["--filter", filter];

    /// <summary>
    /// Splits a raw options string into argv tokens on whitespace, respecting double-quoted segments (so
    /// e.g. <c>--filter-method "My Test"</c> becomes two tokens, not four). Used to turn a string built for
    /// display/length-checking (<see cref="TestingPlatformNameFilter"/>) or a caller-supplied "verbatim"
    /// options string into individual <see cref="ProcessStartInfo.ArgumentList"/> entries.
    /// </summary>
    internal static IReadOnlyList<string> SplitArgs(string raw) => SplitArgsCore(raw);

    /// <summary>
    /// Under Microsoft.Testing.Platform the filter is passed as the test framework's own options, split into arguments.
    /// `dotnet test` would also accept MSBuild options there (<c>-p:CustomBeforeMicrosoftCommonTargets=...</c> imports a
    /// targets file), so every option in it must be a filter option: <c>--filter*</c> (xUnit v3, MSTest, NUnit) or
    /// <c>--treenode-filter</c> (TUnit). Values between options are left alone. Returns an error, or null if allowed.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex MsBuildSlashSwitch = new(@"^/[A-Za-z][A-Za-z0-9-]*([:=].*)?$");

    public static string? ValidateTestingPlatformFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return null;
        foreach (var token in SplitArgsCore(filter))
        {
            // A value (method name, TUnit tree path like /*/*/MyClass/*) passes. MSBuild also takes Windows-style switches
            // (/p:X=1), so a '/' token that is shaped like one (/word, or /word: or /word= followed by anything) counts as an option; tree paths continue with '/' or '*' instead.
            if (!token.StartsWith('-') && !MsBuildSlashSwitch.IsMatch(token)) continue;
            var option = token.Split('=', ':')[0];
            if (!option.StartsWith("--filter", StringComparison.Ordinal) && option != "--treenode-filter")
            {
                return $"Invalid filter option '{token}': under Microsoft.Testing.Platform only --filter* and --treenode-filter options are allowed.";
            }
        }
        return null;
    }

    private static IReadOnlyList<string> SplitArgsCore(string raw)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        foreach (var c in raw)
        {
            if (c == '"') { inQuotes = !inQuotes; hasToken = true; continue; }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken) { result.Add(current.ToString()); current.Clear(); hasToken = false; }
                continue;
            }
            current.Append(c);
            hasToken = true;
        }
        if (hasToken) result.Add(current.ToString());
        return result;
    }

    private static string Tail(string s, int lines = 40)
    {
        var all = s.Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
    }

    /// <summary>
    /// Where to start `dotnet` for a path. The CLI reads global.json from its working directory: the pinned SDK and the
    /// test runner mode (VSTest or Microsoft.Testing.Platform) both come from there, not from the project's location.
    /// </summary>
    public static string DirectoryOf(string path) => Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path))!;

    internal static Task<(int ExitCode, string Stdout, string Stderr, bool TimedOut)> RunDotnetAsync(IReadOnlyList<string> arguments, CancellationToken ct, string? workingDirectory = null, TimeSpan? timeout = null)
        => RunProcessAsync("dotnet", arguments, ct, workingDirectory, timeout);

    /// <summary>
    /// Runs a child with all three std handles redirected: in stdio mode the parent's stdin/stdout ARE the MCP channel.
    /// A run must always return, so <paramref name="timeout"/> bounds it: past it, the whole process tree is killed
    /// (a child dotnet/testhost survives its parent otherwise) and TimedOut comes back true instead of throwing.
    /// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, one entry per value, so .NET does the
    /// quoting and nothing can smuggle in extra arguments the way concatenating a single argument string would allow.
    /// </summary>
    internal static async Task<(int ExitCode, string Stdout, string Stderr, bool TimedOut)> RunProcessAsync(
        string fileName, IReadOnlyList<string> arguments, CancellationToken ct, string? workingDirectory = null, TimeSpan? timeout = null,
        System.Text.Encoding? outputEncoding = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = outputEncoding,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en"; // we grep "The following Tests are available"
        ChildProcess.Prepare(psi);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}");
        p.StandardInput.Close();
        // Read against `ct` only, never the timeout token: killing the process closes these pipes (EOF), so both tasks
        // complete on their own after a timeout kill. Waiting on them with a timeout token too would just add a deadlock risk.
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        var timedOut = false;
        using var timeoutCts = timeout is { } t ? new CancellationTokenSource(t) : null;
        using var waitCts = timeoutCts is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await p.WaitForExitAsync(waitCts?.Token ?? ct);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !ct.IsCancellationRequested)
        {
            timedOut = true;
            try { p.Kill(entireProcessTree: true); await p.WaitForExitAsync(CancellationToken.None); } catch { /* already gone */ }
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        if (timedOut)
        {
            // A process outside the killed tree (an MSBuild node, say) can still hold the pipes; take what arrived, don't wait on it.
            await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None));
            return (-1, stdout.IsCompletedSuccessfully ? stdout.Result : "", stderr.IsCompletedSuccessfully ? stderr.Result : "", true);
        }
        return (p.ExitCode, await stdout, await stderr, false);
    }
}
