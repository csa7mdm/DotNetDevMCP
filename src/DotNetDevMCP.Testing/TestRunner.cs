// Copyright (c) 2025 Ahmed Mustafa

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
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
        var (exit, stdout, stderr) = await RunDotnetAsync($"test \"{projectPath}\" --list-tests{FilterArg(filter)}", ct, DirectoryOf(projectPath));
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
    public async Task<TestRunSummary> RunAsync(string projectOrSolutionPath, string? filter, IReadOnlyCollection<string>? testNames, bool noBuild, string? framework, CancellationToken ct)
    {
        projectOrSolutionPath = Path.GetFullPath(projectOrSolutionPath);
        return UsesTestingPlatform(projectOrSolutionPath)
            ? await RunTestingPlatformAsync(projectOrSolutionPath, filter, testNames, noBuild, framework, ct)
            : await RunVsTestAsync(projectOrSolutionPath, filter, testNames, noBuild, framework, ct);
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

    private async Task<TestRunSummary> RunTestingPlatformAsync(string path, string? filter, IReadOnlyCollection<string>? testNames, bool noBuild, string? framework, CancellationToken ct)
    {
        var target = path.EndsWith("proj", StringComparison.OrdinalIgnoreCase) ? "--project"
            : Directory.Exists(path) ? "--directory" : "--solution";
        var sw = Stopwatch.StartNew();
        (int ExitCode, string Stdout, string Stderr) run = default;
        foreach (var xunit in IsXunitByPath.TryGetValue(path, out var known) ? new[] { known } : new[] { true, false })
        {
            var args = new StringBuilder($"test {target} \"{path}\"");
            if (noBuild) args.Append(" --no-build");
            if (!string.IsNullOrWhiteSpace(framework)) args.Append($" --framework {framework}");
            args.Append(xunit ? " --report-xunit-trx" : " --report-trx");
            args.Append(TestingPlatformNameFilter(testNames, xunit));
            if (!string.IsNullOrWhiteSpace(filter)) args.Append(' ').Append(filter); // the framework's own filter options, verbatim
            run = await RunDotnetAsync(args.ToString(), ct, DirectoryOf(path));
            if (run.ExitCode != MtpInvalidCommandLine) { IsXunitByPath[path] = xunit; break; }
            noBuild = true; // the first attempt already built
        }
        sw.Stop();

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

    private async Task<TestRunSummary> RunVsTestAsync(string projectOrSolutionPath, string? filter, IReadOnlyCollection<string>? testNames, bool noBuild, string? framework, CancellationToken ct)
    {
        var resultsDir = Path.Combine(Path.GetTempPath(), "dotnetdevmcp-trx", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsDir);

        var fullFilter = filter;
        if (testNames is { Count: > 0 })
        {
            // ponytail: OR of exact names. Command lines cap around 32k chars on Windows; ~200 long names is the practical ceiling,
            // above that callers should run the whole project.
            var byName = string.Join("|", testNames.Select(n => $"FullyQualifiedName={n}"));
            fullFilter = string.IsNullOrEmpty(filter) ? byName : $"({filter})&({byName})";
        }

        var args = new StringBuilder($"test \"{projectOrSolutionPath}\" --logger trx --results-directory \"{resultsDir}\"");
        if (noBuild) args.Append(" --no-build");
        if (!string.IsNullOrWhiteSpace(framework)) args.Append($" --framework {framework}");
        args.Append(FilterArg(fullFilter));

        var sw = Stopwatch.StartNew();
        var (exit, stdout, stderr) = await RunDotnetAsync(args.ToString(), ct, DirectoryOf(projectOrSolutionPath));
        sw.Stop();

        var trxFiles = Directory.GetFiles(resultsDir, "*.trx", SearchOption.AllDirectories);
        if (trxFiles.Length == 0)
        {
            // Build failure or bad path: exit != 0 and nothing written. "No test is available" also lands here.
            return TestRunSummary.Failed(exit == 0 ? "dotnet test ran but wrote no results (no tests matched?)" : Tail(stdout + stderr), sw.Elapsed);
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

    private static string FilterArg(string? filter) => string.IsNullOrWhiteSpace(filter) ? "" : $" --filter \"{filter.Replace("\"", "\\\"")}\"";

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

    internal static Task<(int ExitCode, string Stdout, string Stderr)> RunDotnetAsync(string arguments, CancellationToken ct, string? workingDirectory = null)
        => RunProcessAsync("dotnet", arguments, ct, workingDirectory);

    /// <summary>Runs a child with all three std handles redirected: in stdio mode the parent's stdin/stdout ARE the MCP channel.</summary>
    internal static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(string fileName, string arguments, CancellationToken ct, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en"; // we grep "The following Tests are available"
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}");
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        return (p.ExitCode, await stdout, await stderr);
    }
}
