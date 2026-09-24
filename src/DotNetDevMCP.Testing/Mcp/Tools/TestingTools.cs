// Copyright (c) 2025 Ahmed Mustafa

using System.ComponentModel;
using DotNetDevMCP.CodeIntelligence.Interfaces;
using DotNetDevMCP.Core.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotNetDevMCP.Testing.Mcp.Tools;

public sealed class TestingToolsLogCategory { }

[McpServerToolType]
public static class TestingTools
{
    [McpServerTool(Name = "dotnet_test_discover", Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists the tests in a test project (dotnet test --list-tests). Builds the project first unless it is already built.")]
    public static async Task<object> Discover(
        TestRunner runner,
        [Description("Path to the test project (.csproj)")] string projectPath,
        [Description("VSTest filter, e.g. FullyQualifiedName~OrderService or Category=Unit")] string? filter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tests = await runner.DiscoverAsync(projectPath, filter, cancellationToken);
            return new { Success = true, TotalTests = tests.Count, Tests = tests.Select(t => t.FullyQualifiedName) };
        }
        catch (Exception ex)
        {
            return new { Success = false, Error = ex.Message };
        }
    }

    [McpServerTool(Name = "dotnet_test_run", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Runs tests in a project or a whole solution with one dotnet test invocation and returns per-test results, failures with messages and stack traces. Use filter or testNames to narrow.")]
    public static async Task<object> Run(
        TestRunner runner,
        ILogger<TestingToolsLogCategory> logger,
        [Description("Path to a test project (.csproj) or a solution (.sln)")] string path,
        [Description("VSTest filter expression, e.g. FullyQualifiedName~OrderService|Category=Unit")] string? filter = null,
        [Description("Exact fully qualified test names to run (Namespace.Class.Method). Combined with filter if both given.")] string[]? testNames = null,
        [Description("Skip the build. Only when nothing changed since the last build.")] bool noBuild = false,
        [Description("Run one target framework only, e.g. net10.0. Default: every framework the projects target.")] string? framework = null,
        [Description("Kill the run and fail it past this many seconds. A run must always return, even if a test hangs (e.g. an injected fault causing a deadlock). Default 600.")] int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        var summary = await runner.RunAsync(path, filter, testNames, noBuild, framework, cancellationToken, timeoutSeconds);
        logger.LogInformation("dotnet_test_run {Path}: {Passed}/{Total} passed in {Sec:F1}s", path, summary.PassedTests, summary.TotalTests, summary.Duration.TotalSeconds);
        return Shape(summary);
    }

    [McpServerTool(Name = "dotnet_test_affected", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Finds the tests that reference the code in the changed files (via Roslyn, through the loaded solution) and runs only those. Default changed files: the git working tree. Requires a loaded solution (SharpTool_LoadSolution or --load-solution).")]
    public static async Task<object> RunAffected(
        TestRunner runner,
        AffectedTestFinder finder,
        ISolutionManager solutions,
        ILogger<TestingToolsLogCategory> logger,
        [Description("Changed source files. Omit to use git: uncommitted changes, or the diff against gitBase if given.")] string[]? changedFiles = null,
        [Description("Git ref to diff against instead of the working tree, e.g. main or HEAD~3")] string? gitBase = null,
        [Description("How many reference hops to follow from a changed symbol (1 = tests that call it directly). Default 8.")] int maxDepth = 8,
        [Description("Only report which tests would run; do not run them.")] bool dryRun = false,
        [Description("Skip building the affected test projects. Only when nothing changed since the last build.")] bool noBuild = false,
        [Description("Seconds allowed for tracing. Past it the change reaches too much code for selection to beat running everything, so the whole solution runs instead. Default 10.")] int maxSelectionSeconds = 10,
        [Description("Run one target framework only, e.g. net10.0: much faster for multi-targeted test projects. Default: every framework.")] string? framework = null,
        [Description("Above this share of all test methods, run the whole solution instead of the filtered selection. Measured on Polly: a selection of 4% of tests ran 3.3x faster than the whole suite, but 23% was slower than just running everything (filtered runs plus per-project overhead don't pay for themselves past a point). Default 0.2.")] double maxSelectedFraction = 0.2,
        [Description("Kill the run and fail it past this many seconds. A run must always return, even if a test hangs. Default 600.")] int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        if (!solutions.IsSolutionLoaded)
        {
            return new { Success = false, Error = "No solution loaded. Call SharpTool_LoadSolution first or start the server with --load-solution." };
        }

        var solutionDir = Path.GetDirectoryName(solutions.CurrentSolution.FilePath!)!;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var files = changedFiles is { Length: > 0 } ? changedFiles : await GitChangedFilesAsync(solutionDir, gitBase, cancellationToken);
        logger.LogDebug("dotnet_test_affected: resolved {Count} changed files in {Ms} ms", files.Length, sw.ElapsedMilliseconds);
        files = files.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Select(f => Path.GetFullPath(Path.Combine(solutionDir, f))).ToArray();
        if (files.Length == 0)
        {
            return new { Success = true, ChangedFiles = Array.Empty<string>(), AffectedTests = Array.Empty<object>(), Message = "No changed .cs files." };
        }

        sw.Restart();
        var selection = await finder.FindAsync(files, maxDepth, TimeSpan.FromSeconds(maxSelectionSeconds), cancellationToken);
        var affected = selection.Tests;
        logger.LogInformation("dotnet_test_affected: {Files} changed files -> {Tests} tests, {Symbols} symbols searched, complete={Complete}, {Ms} ms",
            files.Length, affected.Count, selection.SymbolsSearched, selection.Complete, sw.ElapsedMilliseconds);

        var list = affected.Select(a => new { a.FullyQualifiedName, Project = Path.GetFileNameWithoutExtension(a.ProjectPath), a.Via });

        var selectedFraction = selection.TotalTestMethods > 0 ? (double)affected.Count / selection.TotalTestMethods : 0;
        // Two independent reasons to give up on filtering and just run everything: the walk didn't finish (unsafe to trust a
        // partial set), or it did finish but the selection is big enough that running it filtered is likely slower anyway
        // (measured on Polly: ~4% of tests ran 3.3x faster filtered, ~23% was slower than the whole suite).
        var note = !selection.Complete
            ? $"Selection stopped after {maxSelectionSeconds}s and {selection.SymbolsSearched} symbols: this change reaches too much code to trace cheaply. The tests listed are a partial set; a run executes the whole solution instead."
            : selectedFraction > maxSelectedFraction
                ? $"Selected {affected.Count} of {selection.TotalTestMethods} test methods ({selectedFraction:P0}), above the {maxSelectedFraction:P0} threshold: a selection this large is likely slower filtered than running the whole solution. Running the whole solution instead."
                : null;
        var ranWholeSolution = note is not null;

        if (dryRun || (selection.Complete && affected.Count == 0))
        {
            return new { Success = true, ChangedFiles = files, SelectionComplete = selection.Complete, selection.SymbolsSearched, selection.TotalTestMethods, Note = note, RanWholeSolution = ranWholeSolution, AffectedTests = list, Ran = false };
        }

        TestRunSummary summary;
        if (ranWholeSolution)
        {
            // Incomplete selection, or a selection too large to be worth filtering: the safe/fast answer is everything. One solution-wide run, which builds it once.
            summary = await runner.RunAsync(solutions.CurrentSolution.FilePath!, null, null, noBuild, framework, cancellationToken, timeoutSeconds);
        }
        else
        {
            var byProject = affected.GroupBy(a => a.ProjectPath).ToList();

            // Build one project at a time: test projects share references, and parallel builds of the same outputs collide on file locks.
            if (!noBuild)
            {
                foreach (var project in byProject.Select(g => g.Key))
                {
                    var tfm = string.IsNullOrWhiteSpace(framework) ? "" : $" --framework {framework}";
                    var (exit, stdout, stderr, _) = await TestRunner.RunDotnetAsync($"build \"{project}\" -nologo{tfm}", cancellationToken, TestRunner.DirectoryOf(project));
                    if (exit != 0)
                    {
                        return new { Success = false, ChangedFiles = files, AffectedTests = list, Ran = false, Error = $"Build failed for {Path.GetFileName(project)}:\n{BuildErrors(stdout + stderr)}" };
                    }
                }
            }

            // Then one dotnet test per affected project, in parallel.
            var runs = await Task.WhenAll(byProject.Select(g => runner.RunAsync(g.Key, null, g.Select(a => a.FullyQualifiedName).ToList(), noBuild: true, framework, cancellationToken, timeoutSeconds)));
            summary = TestRunSummary.Merge(runs);
        }

        return new { Success = summary.Success, ChangedFiles = files, SelectionComplete = selection.Complete, selection.SymbolsSearched, selection.TotalTestMethods, Note = note, RanWholeSolution = ranWholeSolution, AffectedTests = list, Ran = true, Run = Shape(summary) };
    }

    private static string BuildErrors(string output)
    {
        var errors = output.Split('\n').Select(l => l.Trim()).Where(l => l.Contains(": error ")).Distinct().Take(20).ToList();
        return errors.Count > 0 ? string.Join("\n", errors) : string.Join("\n", output.Split('\n').Where(l => l.Trim().Length > 0).TakeLast(20));
    }

    private static object Shape(TestRunSummary s) => new
    {
        s.Success,
        s.Error,
        s.TotalTests,
        s.PassedTests,
        s.FailedTests,
        s.SkippedTests,
        DurationSeconds = Math.Round(s.Duration.TotalSeconds, 1),
        Failures = s.Failures.Select(f => new { f.FullyQualifiedName, f.ErrorMessage, f.StackTrace, f.Output }),
    };

    private static async Task<string[]> GitChangedFilesAsync(string repoDir, string? gitBase, CancellationToken ct)
    {
        var args = gitBase is null ? "status --porcelain --untracked-files=all" : $"diff --name-only {gitBase}";
        var (exit, output, err, _) = await TestRunner.RunProcessAsync("git", args, ct, repoDir);
        if (exit != 0) throw new InvalidOperationException($"git {args} failed: {err.Trim()}");
        var (_, root, _, _) = await TestRunner.RunProcessAsync("git", "rev-parse --show-toplevel", ct, repoDir);
        root = root.Trim().Length == 0 ? repoDir : root.Trim();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => gitBase is null ? l[3..].Trim() : l.Trim())     // porcelain lines are "XY path"
            .Select(rel => Path.Combine(root, rel))
            .ToArray();
    }
}
