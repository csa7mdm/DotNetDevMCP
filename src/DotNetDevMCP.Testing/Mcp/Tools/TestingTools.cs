// Copyright (c) 2025 Ahmed Mustafa

using System.ComponentModel;
using System.Xml.Linq;
using DotNetDevMCP.CodeIntelligence.Interfaces;
using DotNetDevMCP.Core;
using DotNetDevMCP.Core.Models;
using Microsoft.CodeAnalysis;
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
        [Description("VSTest filter expression, e.g. FullyQualifiedName~OrderService|Category=Unit. For a project that runs under Microsoft.Testing.Platform, this is instead that test framework's own filter options, e.g. xUnit v3's `--filter-class My.Tests`; only --filter* and --treenode-filter options are accepted.")] string? filter = null,
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

        var gitBaseError = GitRefValidation.Validate(gitBase, nameof(gitBase));
        if (gitBaseError != null) return new { Success = false, Error = gitBaseError };

        var frameworkError = DotnetArgumentValidation.ValidateFramework(framework);
        if (frameworkError != null) return new { Success = false, Error = frameworkError };

        var solutionDir = Path.GetDirectoryName(solutions.CurrentSolution.FilePath!)!;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var files = changedFiles is { Length: > 0 } ? changedFiles : await GitChangedFilesAsync(solutionDir, gitBase, cancellationToken);
        logger.LogDebug("dotnet_test_affected: resolved {Count} changed files in {Ms} ms", files.Length, sw.ElapsedMilliseconds);
        var solution = solutions.CurrentSolution;
        var changed = files.Select(f => Path.GetFullPath(Path.Combine(solutionDir, f)))
            .Where(f => !IsIgnoredDocumentation(solution, solutionDir, f)).ToArray();
        // Never traced at any layer (not a document, not a project-owned file, not build-wide): noted, not analyzed.
        var dllChanged = changed.Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        // The walk traces only C# the solution compiles. Anything else (a .csproj, .razor, appsettings.json, a deleted file)
        // can still break tests, so it switches to the project fallback below instead of being dropped.
        files = changed.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !solution.GetDocumentIdsWithFilePath(f).IsEmpty).ToArray();
        var untraced = changed.Where(f => !files.Contains(f) && (AffectedTestFinder.OwningProjects(solution, f).Count > 0 || IsBuildWideFile(f))).ToArray();
        var buildWideChange = untraced.Any(IsBuildWideFile);
        if (files.Length == 0 && untraced.Length == 0)
        {
            var message = "No changed code or project files.";
            if (dllChanged.Length > 0) message += " Binary references (.dll) are not traced.";
            return new { Success = true, ChangedFiles = Array.Empty<string>(), AffectedTests = Array.Empty<object>(), Message = message };
        }

        sw.Restart();
        var selection = await finder.FindAsync(files, maxDepth, TimeSpan.FromSeconds(maxSelectionSeconds), cancellationToken);
        var affected = selection.Tests;
        logger.LogInformation("dotnet_test_affected: {Files} changed files -> {Tests} tests, {Symbols} symbols searched, complete={Complete}, {Ms} ms",
            files.Length, affected.Count, selection.SymbolsSearched, selection.Complete, sw.ElapsedMilliseconds);

        var list = affected.Select(a => new { a.FullyQualifiedName, Project = Path.GetFileNameWithoutExtension(a.ProjectPath), a.Via });

        var selectedFraction = selection.TotalTestMethods > 0 ? (double)affected.Count / selection.TotalTestMethods : 0;
        // Two independent reasons to give up on filtering: the walk didn't finish (unsafe to trust a partial set), or it did
        // finish but the selection is big enough that running it filtered is likely slower anyway (measured on Polly: ~4% of
        // tests ran 3.3x faster filtered, ~23% was slower than the whole suite). Either way, fall back to running whole test
        // projects picked from the (cheap) project reference graph instead of jumping straight to the whole solution - a
        // change to one library rarely reaches every test project (measured on Polly: 7 test projects, usually 1-2 affected).
        string? note;
        AffectedRunScope scope;
        IReadOnlyList<string> projectsToRun = [];
        IReadOnlyList<string> allTestProjects = [];
        var assetsCache = new AffectedTestFinder.AssetsCache();
        if (untraced.Length == 0 && selection.Complete && selectedFraction <= maxSelectedFraction)
        {
            scope = AffectedRunScope.Selection;
            note = null;
        }
        else
        {
            var reason = untraced.Length > 0
                ? $"Changed files the reference walk can't trace ({string.Join(", ", untraced.Select(Path.GetFileName))}): tests that depend on them can't be picked by name."
                : !selection.Complete
                ? $"Selection stopped after {maxSelectionSeconds}s and {selection.SymbolsSearched} symbols: this change reaches too much code to trace cheaply."
                : $"Selected {affected.Count} of {selection.TotalTestMethods} test methods ({selectedFraction:P0}), above the {maxSelectedFraction:P0} threshold: a selection this large is likely slower filtered than running the whole solution.";

            // Central Package Management precision: a Directory.Packages.props change is build-wide in general (it can
            // affect any project), so it's only narrowed when it is the ONLY changed file of any kind (a build-wide
            // props change alongside a traced .cs change, or any other file, keeps the pre-existing build-wide ->
            // whole-solution behavior below - merging a package-version diff with a symbol-level selection would be a
            // different, riskier feature). Diffing the file's previous and current XML then tells us exactly which
            // package ids moved, and the reverse dependency graph (ProjectReference + package edges, walked from every
            // solution project - not just test projects - whose restored assets reference one of them) tells us
            // exactly which runnable test projects to run instead of the whole solution.
            var cpmOnly = changed.Length == 1
                && IsBuildWideFile(changed[0])
                && Path.GetFileName(changed[0]).Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase);
            var cpm = cpmOnly ? await TryCentralPackageManagementScopeAsync(solution, solutionDir, changed[0], gitBase, assetsCache, cancellationToken) : null;

            // Skipped entirely for the cpmOnly case: a lone build-wide change can never land in the reachableProjects
            // branch below (buildWideChange always routes it to Solution or the CPM Projects scope first), so
            // computing it would only cost an unused pass over the project graph and assets files.
            var reachableProjects = cpmOnly
                ? (IReadOnlyList<string>)Array.Empty<string>()
                : AffectedTestFinder.FindAffectedTestProjects(solution, files.Concat(untraced), assetsCache);
            allTestProjects = AffectedTestFinder.AllTestProjectFilePaths(solution);

            if (cpm is { Projects.Count: > 0 } cpmHit && cpmHit.Projects.Count < allTestProjects.Count)
            {
                scope = AffectedRunScope.Projects;
                projectsToRun = cpmHit.Projects;
                note = cpmHit.Note;
            }
            else if (cpm is { Projects.Count: > 0 } cpmCoversAll)
            {
                // Narrowed set happens to be every runnable test project: one solution-wide run is simpler than
                // filtered per-project runs that would add up to the same coverage, exactly like the non-CPM fallback
                // below already prefers Solution once reachableProjects covers everything.
                scope = AffectedRunScope.Solution;
                note = $"Directory.Packages.props changed: {cpmCoversAll.IdsSummary}; every runnable test project uses at least one of them, so running the whole solution instead.";
            }
            else if (buildWideChange || reachableProjects.Count == 0 || reachableProjects.Count >= allTestProjects.Count)
            {
                scope = AffectedRunScope.Solution;
                note = cpm is { } cpmMiss
                    ? $"{reason} {cpmMiss.Note}"
                    : buildWideChange
                    ? $"{reason} A build-wide file changed, which can affect every project; running the whole solution instead."
                    : $"{reason} Every test project in the solution is reachable from the change (or none could be resolved to a project); running the whole solution instead.";
            }
            else
            {
                scope = AffectedRunScope.Projects;
                projectsToRun = reachableProjects;
                var names = string.Join(", ", reachableProjects.Select(Path.GetFileName));
                note = $"{reason} Running the test projects reachable from the change via project references instead: {names}. " +
                    "This is a superset of the affected tests (some of their other tests may also run); a test project " +
                    "that uses the changed code through a NuGet package is found only if it has been restored " +
                    "(obj/project.assets.json); cross-repository consumers are not found.";
            }
        }

        if (scope == AffectedRunScope.Projects)
        {
            var unrestoredCount = AffectedTestFinder.CountUnrestoredTestProjects(solution, assetsCache);
            if (unrestoredCount > 0)
            {
                note = $"{note} {unrestoredCount} test project(s) have no readable obj/project.assets.json (not restored or unreadable), so package references for them are unknown.";
            }
        }
        if (dllChanged.Length > 0)
        {
            note = note is null ? "Binary references (.dll) are not traced." : $"{note} Binary references (.dll) are not traced.";
        }
        var ranWholeSolution = scope == AffectedRunScope.Solution; // kept for compatibility; true only when the whole solution ran.

        if (dryRun || (scope == AffectedRunScope.Selection && affected.Count == 0))
        {
            var plannedProjects = scope switch
            {
                AffectedRunScope.Projects => projectsToRun,
                AffectedRunScope.Solution => allTestProjects,
                _ => (IReadOnlyList<string>)[],
            };
            return new { Success = true, ChangedFiles = files, UntracedFiles = untraced, SelectionComplete = selection.Complete, selection.SymbolsSearched, selection.TotalTestMethods, Note = note, RanWholeSolution = ranWholeSolution, RanScope = ScopeName(scope), TestProjectsRun = plannedProjects.Select(Path.GetFileName), AffectedTests = list, Ran = false };
        }

        TestRunSummary summary;
        IReadOnlyList<string> testProjectsRun;
        switch (scope)
        {
            case AffectedRunScope.Solution:
                // Incomplete/too-large selection and the project-reachability fallback still covers everything: the
                // safe/fast answer is everything. One solution-wide run, which builds it once.
                summary = await runner.RunAsync(solutions.CurrentSolution.FilePath!, null, null, noBuild, framework, cancellationToken, timeoutSeconds);
                testProjectsRun = allTestProjects;
                break;

            case AffectedRunScope.Projects:
                // Project-level fallback: build the reachable test projects one at a time (they share references; parallel
                // builds of the same outputs collide on file locks), then run all of them in parallel with no name filter -
                // every test in each of these projects runs, not just the ones the symbol walk would have picked.
                if (!noBuild && await BuildEachAsync(projectsToRun, framework, cancellationToken) is { } projectBuildError)
                {
                    return new { Success = false, ChangedFiles = files, AffectedTests = list, Ran = false, Error = projectBuildError };
                }
                var projectRuns = await Task.WhenAll(projectsToRun.Select(p => runner.RunAsync(p, null, null, noBuild: true, framework, cancellationToken, timeoutSeconds)));
                summary = TestRunSummary.Merge(projectRuns);
                testProjectsRun = projectsToRun;
                break;

            default: // Selection
                var byProject = affected.GroupBy(a => a.ProjectPath).ToList();

                if (!noBuild && await BuildEachAsync(byProject.Select(g => g.Key), framework, cancellationToken) is { } selectionBuildError)
                {
                    return new { Success = false, ChangedFiles = files, AffectedTests = list, Ran = false, Error = selectionBuildError };
                }

                // Then one dotnet test per affected project, in parallel.
                var runs = await Task.WhenAll(byProject.Select(g => runner.RunAsync(g.Key, null, g.Select(a => a.FullyQualifiedName).ToList(), noBuild: true, framework, cancellationToken, timeoutSeconds)));
                summary = TestRunSummary.Merge(runs);
                testProjectsRun = byProject.Select(g => g.Key).ToList();
                break;
        }

        return new { Success = summary.Success, ChangedFiles = files, UntracedFiles = untraced, SelectionComplete = selection.Complete, selection.SymbolsSearched, selection.TotalTestMethods, Note = note, RanWholeSolution = ranWholeSolution, RanScope = ScopeName(scope), TestProjectsRun = testProjectsRun.Select(Path.GetFileName), AffectedTests = list, Ran = true, Run = Shape(summary) };
    }

    /// <summary>Documentation extensions; see <see cref="IsIgnoredDocumentation"/>. ponytail: extension list, not content sniffing.</summary>
    private static readonly HashSet<string> DocumentationExtensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".txt", ".png", ".jpg", ".jpeg", ".gif", ".svg" };

    /// <summary>
    /// A changed file that can't break a test: .md anywhere, or another documentation extension outside every project
    /// folder. A .txt/.png inside a project can be test data (TestData/expected.txt, Verify's *.verified.txt), so it
    /// counts. A project at the solution root would "own" every file (docs/, README.md), so its ownership doesn't count.
    /// </summary>
    private static bool IsIgnoredDocumentation(Solution solution, string solutionDir, string path)
    {
        var ext = Path.GetExtension(path);
        if (!DocumentationExtensions.Contains(ext)) return false;
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase)) return true;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(solutionDir));
        return !AffectedTestFinder.OwningProjects(solution, path).Select(solution.GetProject).OfType<Project>()
            .Any(p => p.FilePath is { } fp && !string.Equals(Path.GetDirectoryName(Path.GetFullPath(fp)), root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Files outside any project folder that still feed every build.</summary>
    private static bool IsBuildWideFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
            || name.Equals("global.json", StringComparison.OrdinalIgnoreCase) || name.Equals("nuget.config", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase);
    }

    private static string ScopeName(AffectedRunScope scope) => scope.ToString().ToLowerInvariant();

    /// <summary>Builds test projects one at a time (they share references; parallel builds of the same outputs collide on file
    /// locks). Returns the first build's errors, or null when every build succeeded.</summary>
    private static async Task<string?> BuildEachAsync(IEnumerable<string> projects, string? framework, CancellationToken ct)
    {
        foreach (var project in projects)
        {
            var args = new List<string> { "build", project, "-nologo" };
            if (!string.IsNullOrWhiteSpace(framework)) { args.Add("--framework"); args.Add(framework); }
            var (exit, stdout, stderr, _) = await TestRunner.RunDotnetAsync(args, ct, TestRunner.DirectoryOf(project));
            if (exit != 0) return $"Build failed for {Path.GetFileName(project)}:\n{BuildErrors(stdout + stderr)}";
        }
        return null;
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

    // gitBase is validated by the caller (RunAffected) with GitRefValidation before this ever runs; validating
    // again here would be redundant, but the ArgumentList below is what actually keeps it from being parsed as
    // a git option (e.g. "--output=C:/x.txt") the way concatenating it into a single argument string would allow.
    private static async Task<string[]> GitChangedFilesAsync(string repoDir, string? gitBase, CancellationToken ct)
    {
        List<string> args = gitBase is null
            ? ["status", "--porcelain", "--untracked-files=all"]
            : ["diff", "--name-only", gitBase];
        var (exit, output, err, _) = await TestRunner.RunProcessAsync("git", args, ct, repoDir);
        if (exit != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {err.Trim()}");
        var (_, root, _, _) = await TestRunner.RunProcessAsync("git", ["rev-parse", "--show-toplevel"], ct, repoDir);
        root = root.Trim().Length == 0 ? repoDir : root.Trim();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => gitBase is null ? l[3..].Trim() : l.Trim())     // porcelain lines are "XY path"
            .Select(rel => Path.Combine(root, rel))
            .ToArray();
    }

    /// <summary>Result of <see cref="TryCentralPackageManagementScopeAsync"/>: which test projects to run and the note
    /// to report for it. Projects is empty when precision wasn't possible - Note then explains why, and the caller
    /// falls back to running the whole solution exactly as it did before this precision existed. IdsSummary is the
    /// comma-joined changed package ids (empty until they're known), reused by the caller to phrase its own note when
    /// the narrowed set happens to cover every runnable test project.</summary>
    private sealed record CpmScope(IReadOnlyList<string> Projects, string IdsSummary, string Note);

    /// <summary>
    /// Central Package Management precision for a Directory.Packages.props change: diffs its previous and current
    /// version (via git) to the package ids that were actually added, removed, or changed version - refusing to
    /// narrow at all if anything else in the file changed too (see <see cref="IsOnlyPackageVersionChange"/>) - then
    /// selects the runnable test projects reachable from every solution project whose restored assets reference one
    /// of those ids (<see cref="AffectedTestFinder.FindTestProjectsForPackageChange"/>). Falls back to an empty
    /// result - explained in its Note - when git isn't available, either version of the file can't be read or
    /// parsed, something other than a package version changed, any solution project isn't restored, or no restored
    /// project uses the changed ids; the caller then runs the whole solution exactly as it did before this precision
    /// existed.
    /// </summary>
    private static async Task<CpmScope> TryCentralPackageManagementScopeAsync(
        Solution solution, string solutionDir, string propsFilePath, string? gitBase, AffectedTestFinder.AssetsCache assetsCache, CancellationToken ct)
    {
        const string fallbackSuffix = "running the whole solution instead.";
        string newXml;
        try
        {
            newXml = await File.ReadAllTextAsync(propsFilePath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CpmScope([], "", $"Directory.Packages.props changed but could not be read; {fallbackSuffix}");
        }

        // Same argument-list git invocation the file already uses (GitChangedFilesAsync): an ArgumentList, not a
        // concatenated string, so a ref that starts with "-" can't be parsed as an option.
        var (rootExit, rootOut, _, _) = await TestRunner.RunProcessAsync("git", ["rev-parse", "--show-toplevel"], ct, solutionDir);
        if (rootExit != 0)
        {
            return new CpmScope([], "", $"Directory.Packages.props changed but git is not available to diff it; {fallbackSuffix}");
        }
        var root = rootOut.Trim();
        var relative = Path.GetRelativePath(root, propsFilePath).Replace('\\', '/');
        var gitRef = gitBase ?? "HEAD";

        var (showExit, showOut, showErr, _) = await TestRunner.RunProcessAsync("git", ["show", $"{gitRef}:{relative}"], ct, root, outputEncoding: System.Text.Encoding.UTF8);
        showOut = showOut.TrimStart('﻿');
        if (showExit != 0)
        {
            return new CpmScope([], "", $"Directory.Packages.props changed but its previous version ({gitRef}:{relative}) could not be read from git ({showErr.Trim()}); {fallbackSuffix}");
        }

        var onlyVersionsChanged = IsOnlyPackageVersionChange(showOut, newXml);
        if (onlyVersionsChanged is null)
        {
            return new CpmScope([], "", $"Directory.Packages.props changed but its XML could not be diffed; {fallbackSuffix}");
        }
        if (onlyVersionsChanged == false)
        {
            return new CpmScope([], "", $"Directory.Packages.props changed beyond package versions; {fallbackSuffix}");
        }

        var changedIds = DiffPackageVersions(showOut, newXml);
        if (changedIds is null)
        {
            // Can't happen given onlyVersionsChanged was true above (both documents parsed), but keep the same safe
            // fallback rather than assuming.
            return new CpmScope([], "", $"Directory.Packages.props changed but its XML could not be diffed; {fallbackSuffix}");
        }
        var idsSummary = string.Join(", ", changedIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        if (changedIds.Count == 0)
        {
            return new CpmScope([], idsSummary, $"Directory.Packages.props changed but no package version actually differs between {gitRef} and the working tree; {fallbackSuffix}");
        }

        var impact = AffectedTestFinder.FindTestProjectsForPackageChange(solution, changedIds, assetsCache);
        if (impact.UnrestoredProjectPath is { } unrestored)
        {
            return new CpmScope([], idsSummary, $"Directory.Packages.props changed but {Path.GetFileName(unrestored)} has no readable obj/project.assets.json (not restored or unreadable), so package usage can't be checked for the whole solution; {fallbackSuffix}");
        }
        if (impact.TestProjects.Count == 0)
        {
            var reason = impact.UsingProjects is { Count: > 0 } users
                ? $"they are used by {string.Join(", ", users.Select(Path.GetFileName))}, but no runnable test project reaches those"
                : "no restored project references those packages";
            return new CpmScope([], idsSummary, $"Directory.Packages.props changed ({idsSummary}) but {reason}; {fallbackSuffix}");
        }

        return new CpmScope(impact.TestProjects, idsSummary, $"Directory.Packages.props changed: {idsSummary}; running the test projects that use them.");
    }

    /// <summary>
    /// Package ids whose &lt;PackageVersion Include="X" Version="V" /&gt; entry was added, removed, or changed
    /// version (only a changed version can lead to narrowing: an added or removed entry already fails
    /// <see cref="IsOnlyPackageVersionChange"/>, so the caller runs the whole solution) between two versions of a Directory.Packages.props file's XML (namespace-agnostic: an SDK-style props
    /// file declares no xmlns, but this tolerates one if present). Pure and synchronous, so it's unit-testable
    /// without git or the filesystem. Null - not throwing - when either document fails to parse as XML: the caller
    /// falls back to today's whole-solution behavior for a props file it can't safely diff. Reports only the id
    /// diff - whether anything ELSE in the file also changed is a separate question, answered by
    /// <see cref="IsOnlyPackageVersionChange"/>, which the caller checks first before trusting this diff enough to
    /// narrow on it.
    /// </summary>
    public static IReadOnlySet<string>? DiffPackageVersions(string oldXml, string newXml)
    {
        var oldVersions = TryParsePackageVersions(oldXml);
        var newVersions = TryParsePackageVersions(newXml);
        if (oldVersions is null || newVersions is null) return null;

        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, version) in newVersions)
        {
            if (!oldVersions.TryGetValue(id, out var oldVersion) || !string.Equals(oldVersion, version, StringComparison.Ordinal)) changed.Add(id);
        }
        foreach (var id in oldVersions.Keys)
        {
            if (!newVersions.ContainsKey(id)) changed.Add(id);
        }
        return changed;
    }

    /// <summary>
    /// Whether two Directory.Packages.props XML documents differ ONLY in the Version attribute values of their
    /// &lt;PackageVersion&gt; elements - the only difference <see cref="DiffPackageVersions"/> is safe to narrow on.
    /// A &lt;PackageReference&gt;, &lt;GlobalPackageReference&gt;, a Condition, a property like
    /// ManagePackageVersionsCentrally, or any other structural change riding along with a version bump makes this
    /// false, since none of those are covered by the id diff and narrowing on it anyway could miss whatever that
    /// other change affects. Comments and whitespace-only text nodes are stripped before comparing, so reformatting
    /// or a comment edit alongside a version bump doesn't count as "beyond versions". Null - not throwing - when
    /// either document fails to parse as XML.
    /// </summary>
    public static bool? IsOnlyPackageVersionChange(string oldXml, string newXml)
    {
        var oldNormalized = TryNormalizeIgnoringPackageVersions(oldXml);
        var newNormalized = TryNormalizeIgnoringPackageVersions(newXml);
        if (oldNormalized is null || newNormalized is null) return null;
        return XNode.DeepEquals(oldNormalized, newNormalized);
    }

    /// <summary>Parses XML and strips comments, whitespace-only text nodes, and the Version attribute of every
    /// &lt;PackageVersion&gt; element (namespace-agnostic, matching <see cref="TryParsePackageVersions"/>), leaving
    /// only what <see cref="IsOnlyPackageVersionChange"/> should actually compare. Null on a parse failure.</summary>
    private static XDocument? TryNormalizeIgnoringPackageVersions(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        doc.DescendantNodes().OfType<XComment>().Remove();
        doc.DescendantNodes().OfType<XText>().Where(t => string.IsNullOrWhiteSpace(t.Value)).Remove();
        foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == "PackageVersion"))
        {
            element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Version")?.Remove();
        }
        return doc;
    }

    private static Dictionary<string, string>? TryParsePackageVersions(string xml)
    {
        try
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "PackageVersion"))
            {
                var include = element.Attribute("Include")?.Value;
                var version = element.Attribute("Version")?.Value;
                if (!string.IsNullOrEmpty(include) && version is not null) result[include] = version;
            }
            return result;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
