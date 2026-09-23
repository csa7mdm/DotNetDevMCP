// Copyright (c) 2025 Ahmed Mustafa

using System.Diagnostics;
using System.Text;
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
        var (exit, stdout, stderr) = await RunDotnetAsync($"test \"{projectPath}\" --list-tests{FilterArg(filter)}", ct);
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
    public async Task<TestRunSummary> RunAsync(string projectOrSolutionPath, string? filter, IReadOnlyCollection<string>? testNames, bool noBuild, CancellationToken ct)
    {
        projectOrSolutionPath = Path.GetFullPath(projectOrSolutionPath);
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
        args.Append(FilterArg(fullFilter));

        var sw = Stopwatch.StartNew();
        var (exit, stdout, stderr) = await RunDotnetAsync(args.ToString(), ct);
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
