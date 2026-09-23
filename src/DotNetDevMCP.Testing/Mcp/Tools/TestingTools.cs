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
        CancellationToken cancellationToken = default)
    {
        var summary = await runner.RunAsync(path, filter, testNames, noBuild, cancellationToken);
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
        [Description("How many reference hops to follow from a changed symbol (1 = tests that call it directly). Default 3.")] int maxDepth = 3,
        [Description("Only report which tests would run; do not run them.")] bool dryRun = false,
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
        var affected = await finder.FindAsync(files, maxDepth, cancellationToken);
        logger.LogInformation("dotnet_test_affected: {Files} changed files -> {Tests} tests in {Ms} ms", files.Length, affected.Count, sw.ElapsedMilliseconds);

        var list = affected.Select(a => new { a.FullyQualifiedName, Project = Path.GetFileNameWithoutExtension(a.ProjectPath), a.Via });
        if (dryRun || affected.Count == 0)
        {
            return new { Success = true, ChangedFiles = files, AffectedTests = list, Ran = false };
        }

        // One dotnet test per affected project, in parallel.
        var runs = await Task.WhenAll(affected
            .GroupBy(a => a.ProjectPath)
            .Select(g => runner.RunAsync(g.Key, null, g.Select(a => a.FullyQualifiedName).ToList(), noBuild: false, cancellationToken)));
        var summary = TestRunSummary.Merge(runs);

        return new { Success = summary.Success, ChangedFiles = files, AffectedTests = list, Ran = true, Run = Shape(summary) };
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
        var (exit, output, err) = await TestRunner.RunProcessAsync("git", args, ct, repoDir);
        if (exit != 0) throw new InvalidOperationException($"git {args} failed: {err.Trim()}");
        var (_, root, _) = await TestRunner.RunProcessAsync("git", "rev-parse --show-toplevel", ct, repoDir);
        root = root.Trim().Length == 0 ? repoDir : root.Trim();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => gitBase is null ? l[3..].Trim() : l.Trim())     // porcelain lines are "XY path"
            .Select(rel => Path.Combine(root, rel))
            .ToArray();
    }
}
