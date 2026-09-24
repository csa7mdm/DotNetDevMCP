// Copyright (c) 2025 Ahmed Mustafa

using System.Diagnostics;
using DotNetDevMCP.CodeIntelligence.Interfaces;
using DotNetDevMCP.Testing.Mcp.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetDevMCP.Testing.Tests;

/// <summary>
/// Code-review fixes for the Central Package Management precision in <see cref="TestingTools.RunAffected"/>,
/// exercised end to end (dryRun) against a real throwaway git repository rather than through the pure XML-diff
/// helpers alone: the scope decision depends on `git show` against a committed baseline, and several of the bugs
/// found in review (BLOCKER 1, MAJOR 2, MAJOR 4, MAJOR 5) only show up once the whole method runs. The repo lives
/// under a temp folder whose name contains a space, since an argument-list git invocation is exactly what a
/// string-concatenation regression would break on such a path.
/// </summary>
public sealed class RunAffectedCpmReviewFixTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnetdevmcp cpm tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try
        {
            // git marks its object files read-only on Windows; clear that before recursive delete or it throws
            // UnauthorizedAccessException instead of removing them.
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch (IOException) { }
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    [Fact]
    public async Task Blocker1_props_change_alongside_a_traced_file_change_runs_the_whole_solution_not_just_the_package_scope()
    {
        var repo = BuildRepo();
        // Make LibA.Tests' OWN assets reference the changed package too (not just LibA's, via the source-project
        // path MAJOR 2 adds): the point of this test is that a SECOND changed file must force the whole solution
        // regardless of how the package is reachable, narrowly or broadly.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(repo.LibATestsCsproj)!, "obj", "project.assets.json"),
            AssetsJson(("Priv.Analyzer", "package")));
        File.WriteAllText(repo.PropsPath, PropsXml("1.1.0", "1.0.0")); // Priv.Analyzer bump

        var result = await CallRunAffected(repo.Solution, [repo.PropsPath, repo.LibASourcePath]);

        // Before the fix: cpmOnly triggered on ANY build-wide-only untraced set, so this whole-solution-worthy change
        // (a props bump AND a traced .cs edit) was narrowed to just the package-reachable projects, silently dropping
        // whatever the .cs edit alone would have reached outside that package's reverse graph.
        Assert.Equal("solution", Prop<string>(result, "RanScope"));
        var note = Prop<string>(result, "Note")!;
        Assert.DoesNotContain("Directory.Packages.props changed:", note);
        var testProjectsRun = Prop<IEnumerable<string>>(result, "TestProjectsRun")!.ToList();
        Assert.Contains(Path.GetFileName(repo.LibATestsCsproj), testProjectsRun);
        Assert.Contains(Path.GetFileName(repo.OtherTestsCsproj), testProjectsRun);
    }

    [Fact]
    public async Task Major2And4_a_package_only_a_source_projects_assets_reference_still_selects_its_test_project_but_skips_the_helper_project()
    {
        var repo = BuildRepo();
        // Priv.Analyzer appears only in LibA's own assets (simulating PrivateAssets="all": it never flows to a
        // downstream test project's assets), reached from LibA.Tests and TestUtils only via ProjectReference.
        File.WriteAllText(repo.PropsPath, PropsXml("1.1.0", "1.0.0"));

        var result = await CallRunAffected(repo.Solution, [repo.PropsPath]);

        Assert.Equal("projects", Prop<string>(result, "RanScope"));
        var testProjectsRun = Prop<IEnumerable<string>>(result, "TestProjectsRun")!.ToList();
        Assert.Equal([Path.GetFileName(repo.LibATestsCsproj)], testProjectsRun);
        Assert.DoesNotContain(Path.GetFileName(repo.TestUtilsCsproj), testProjectsRun); // no [Fact]: not runnable
        Assert.DoesNotContain(Path.GetFileName(repo.OtherTestsCsproj), testProjectsRun); // unrelated package
        Assert.Contains("Priv.Analyzer", Prop<string>(result, "Note"));
    }

    [Fact]
    public async Task Major4_a_narrowed_set_covering_every_runnable_test_project_runs_the_whole_solution_instead()
    {
        var repo = BuildRepo(includeOtherTests: false); // only LibA.Tests is a runnable test project now
        File.WriteAllText(repo.PropsPath, PropsXml("1.1.0", "1.0.0"));

        var result = await CallRunAffected(repo.Solution, [repo.PropsPath]);

        Assert.Equal("solution", Prop<string>(result, "RanScope"));
        Assert.Contains("every runnable test project", Prop<string>(result, "Note"));
    }

    [Fact]
    public async Task Major3_a_non_version_change_riding_along_with_a_version_bump_is_not_narrowed()
    {
        var repo = BuildRepo();
        var withCondition = PropsXml("1.1.0", "1.0.0")
            .Replace("<PackageVersion Include=\"Priv.Analyzer\" Version=\"1.1.0\" />",
                     "<PackageVersion Include=\"Priv.Analyzer\" Version=\"1.1.0\" Condition=\"'$(TargetFramework)'=='net10.0'\" />");
        File.WriteAllText(repo.PropsPath, withCondition);

        var result = await CallRunAffected(repo.Solution, [repo.PropsPath]);

        Assert.Equal("solution", Prop<string>(result, "RanScope"));
        Assert.Contains("Directory.Packages.props changed beyond package versions", Prop<string>(result, "Note"));
    }

    [Fact]
    public async Task Major3_comments_and_whitespace_riding_along_with_a_version_bump_do_not_block_narrowing()
    {
        var repo = BuildRepo();
        var reformatted = PropsXml("1.1.0", "1.0.0").Replace("<ItemGroup>", "<!-- bumping Priv.Analyzer -->\n  <ItemGroup>\n\n  ");
        File.WriteAllText(repo.PropsPath, reformatted);

        var result = await CallRunAffected(repo.Solution, [repo.PropsPath]);

        Assert.Equal("projects", Prop<string>(result, "RanScope"));
        Assert.Contains("Priv.Analyzer", Prop<string>(result, "Note"));
    }

    [Fact]
    public async Task Major5_a_solution_project_with_no_assets_file_anywhere_blocks_narrowing()
    {
        var repo = BuildRepo();
        File.Delete(Path.Combine(Path.GetDirectoryName(repo.OtherTestsCsproj)!, "obj", "project.assets.json"));
        File.WriteAllText(repo.PropsPath, PropsXml("1.1.0", "1.0.0"));

        var result = await CallRunAffected(repo.Solution, [repo.PropsPath]);

        Assert.Equal("solution", Prop<string>(result, "RanScope"));
        var note = Prop<string>(result, "Note")!;
        Assert.Contains("not restored", note);
        Assert.Contains("Other.Tests.csproj", note);
    }

    [Fact]
    public async Task Minor7_the_project_reachability_fallback_note_says_package_edges_need_a_restore()
    {
        var repo = BuildRepo();

        // The .csproj file itself: untraced (not a document the Roslyn walk can trace) but not build-wide, so this
        // takes the general project-reachability fallback, not the CPM path.
        var result = await CallRunAffected(repo.Solution, [repo.LibACsproj]);

        Assert.Equal("projects", Prop<string>(result, "RanScope"));
        Assert.Contains(
            "a test project that uses the changed code through a NuGet package is found only if it has been " +
            "restored (obj/project.assets.json); cross-repository consumers are not found.",
            Prop<string>(result, "Note"));
        var testProjectsRun = Prop<IEnumerable<string>>(result, "TestProjectsRun")!.ToList();
        Assert.Equal([Path.GetFileName(repo.LibATestsCsproj)], testProjectsRun);
    }

    [Fact]
    public async Task Minor8_a_lone_dll_outside_any_project_still_gets_the_binary_reference_sentence()
    {
        var repo = BuildRepo();
        var dllPath = Path.Combine(_root, "somewhere.dll"); // repo root: outside every project folder, not build-wide

        var result = await CallRunAffected(repo.Solution, [dllPath]);

        Assert.Equal("No changed code or project files. Binary references (.dll) are not traced.", Prop<string>(result, "Message"));
    }

    private static T? Prop<T>(object obj, string name)
    {
        var value = obj.GetType().GetProperty(name)?.GetValue(obj) ?? throw new InvalidOperationException($"No property '{name}' on {obj.GetType()}");
        return (T)value;
    }

    private static async Task<object> CallRunAffected(Solution solution, string[] changedFiles, string? gitBase = null)
    {
        var manager = new FakeSolutionManager(solution);
        var finder = new AffectedTestFinder(manager, NullLogger<AffectedTestFinder>.Instance);
        var runner = new TestRunner();
        return await TestingTools.RunAffected(runner, finder, manager, NullLogger<TestingToolsLogCategory>.Instance,
            changedFiles: changedFiles, gitBase: gitBase, dryRun: true);
    }

    private const string DefaultCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private static string PropsXml(string privAnalyzerVersion, string unrelatedVersion) => $"""
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
          <ItemGroup>
            <PackageVersion Include="Priv.Analyzer" Version="{privAnalyzerVersion}" />
            <PackageVersion Include="Unrelated.Package" Version="{unrelatedVersion}" />
          </ItemGroup>
        </Project>
        """;

    private static string AssetsJson(params (string Id, string Type)[] entries)
    {
        var targets = string.Join(",\n", entries.Select(e => $$"""
                "{{e.Id}}/1.0.0": { "type": "{{e.Type}}" }
            """));
        return $$"""
            {
              "targets": {
                "net10.0": {
                  {{targets}}
                }
              }
            }
            """;
    }

    private static void Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        var stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit(15_000);
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
    }

    /// <summary>The on-disk + in-memory pieces of one throwaway repo: LibA (a library carrying the "private" package
    /// directly in its own assets), LibA.Tests (references LibA via ProjectReference only - no direct package
    /// reference of its own), TestUtils (also references LibA, but declares no [Fact] - a helper `dotnet test` can't
    /// run), and optionally Other.Tests (unrelated, its own unrelated package, no path to LibA).</summary>
    private sealed record Repo(Solution Solution, string PropsPath, string LibACsproj, string LibASourcePath,
        string LibATestsCsproj, string TestUtilsCsproj, string OtherTestsCsproj);

    private Repo BuildRepo(bool includeOtherTests = true)
    {
        Directory.CreateDirectory(_root);
        var propsPath = Path.Combine(_root, "Directory.Packages.props");
        File.WriteAllText(propsPath, PropsXml("1.0.0", "1.0.0"));

        var libADir = Path.Combine(_root, "LibA");
        Directory.CreateDirectory(Path.Combine(libADir, "obj"));
        var libACsproj = Path.Combine(libADir, "LibA.csproj");
        File.WriteAllText(libACsproj, DefaultCsproj);
        File.WriteAllText(Path.Combine(libADir, "obj", "project.assets.json"), AssetsJson(("Priv.Analyzer", "package")));
        var libASourcePath = Path.Combine(libADir, "A.cs");
        File.WriteAllText(libASourcePath, "namespace LibA; public class A { public int M() => 1; }");

        var libATestsDir = Path.Combine(_root, "LibA.Tests");
        Directory.CreateDirectory(Path.Combine(libATestsDir, "obj"));
        var libATestsCsproj = Path.Combine(libATestsDir, "LibA.Tests.csproj");
        File.WriteAllText(libATestsCsproj, DefaultCsproj);
        File.WriteAllText(Path.Combine(libATestsDir, "obj", "project.assets.json"), AssetsJson()); // no direct package reference

        var testUtilsDir = Path.Combine(_root, "TestUtils");
        Directory.CreateDirectory(Path.Combine(testUtilsDir, "obj"));
        var testUtilsCsproj = Path.Combine(testUtilsDir, "TestUtils.csproj");
        File.WriteAllText(testUtilsCsproj, DefaultCsproj);
        File.WriteAllText(Path.Combine(testUtilsDir, "obj", "project.assets.json"), AssetsJson());

        string otherTestsCsproj = "";
        string? otherTestsDir = null;
        if (includeOtherTests)
        {
            otherTestsDir = Path.Combine(_root, "Other.Tests");
            Directory.CreateDirectory(Path.Combine(otherTestsDir, "obj"));
            otherTestsCsproj = Path.Combine(otherTestsDir, "Other.Tests.csproj");
            File.WriteAllText(otherTestsCsproj, DefaultCsproj);
            File.WriteAllText(Path.Combine(otherTestsDir, "obj", "project.assets.json"), AssetsJson(("Unrelated.Package", "package")));
        }

        Git(_root, "init", "--initial-branch=main");
        Git(_root, "config", "user.email", "test@example.com");
        Git(_root, "config", "user.name", "Test");
        Git(_root, "add", "-A");
        Git(_root, "commit", "-m", "init");

        var workspace = new AdhocWorkspace();
        var corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var runtime = MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"));
        var xunit = MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location);

        var slnPath = Path.Combine(_root, "Test.sln");
        var solution = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default, slnPath, []));

        var libAId = ProjectId.CreateNewId();
        solution = solution
            .AddProject(ProjectInfo.Create(libAId, VersionStamp.Default, "LibA", "LibA", LanguageNames.CSharp,
                filePath: libACsproj, metadataReferences: [corlib, runtime]))
            .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(libAId), "A.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From("namespace LibA; public class A { public int M() => 1; }"), VersionStamp.Default)),
                filePath: libASourcePath));

        var libATestsId = ProjectId.CreateNewId();
        solution = solution
            .AddProject(ProjectInfo.Create(libATestsId, VersionStamp.Default, "LibA.Tests", "LibA.Tests", LanguageNames.CSharp,
                filePath: libATestsCsproj, metadataReferences: [corlib, runtime, xunit]))
            .AddProjectReference(libATestsId, new ProjectReference(libAId))
            .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(libATestsId), "T.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(
                    "using Xunit; namespace LibA.Tests; public class T { [Fact] public void T1() { _ = new LibA.A().M(); } }"), VersionStamp.Default)),
                filePath: Path.Combine(libATestsDir, "T.cs")));

        var testUtilsId = ProjectId.CreateNewId();
        solution = solution
            .AddProject(ProjectInfo.Create(testUtilsId, VersionStamp.Default, "TestUtils", "TestUtils", LanguageNames.CSharp,
                filePath: testUtilsCsproj, metadataReferences: [corlib, runtime, xunit]))
            .AddProjectReference(testUtilsId, new ProjectReference(libAId))
            .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(testUtilsId), "Fakes.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(
                    "namespace TestUtils; public static class Fakes { public static int One() => 1; }"), VersionStamp.Default)), // no [Fact]: not runnable
                filePath: Path.Combine(testUtilsDir, "Fakes.cs")));

        if (includeOtherTests)
        {
            var otherId = ProjectId.CreateNewId();
            solution = solution
                .AddProject(ProjectInfo.Create(otherId, VersionStamp.Default, "Other.Tests", "Other.Tests", LanguageNames.CSharp,
                    filePath: otherTestsCsproj, metadataReferences: [corlib, runtime, xunit]))
                .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(otherId), "T.cs",
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(
                        "using Xunit; namespace Other.Tests; public class T { [Fact] public void T1() { } }"), VersionStamp.Default)),
                    filePath: Path.Combine(otherTestsDir!, "T.cs")));
        }

        return new Repo(solution, propsPath, libACsproj, libASourcePath, libATestsCsproj, testUtilsCsproj, otherTestsCsproj);
    }

    /// <summary>Minimal ISolutionManager wrapping a hand-built Solution: RunAffected only ever reads CurrentSolution
    /// and IsSolutionLoaded from it in the dryRun path these tests exercise.</summary>
    private sealed class FakeSolutionManager(Solution solution) : ISolutionManager
    {
        public bool IsSolutionLoaded => true;
        public MSBuildWorkspace? CurrentWorkspace => null;
        public Solution? CurrentSolution => solution;
        public Task LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void UnloadSolution() { }
        public Task<ISymbol?> FindRoslynSymbolAsync(string fullyQualifiedName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<INamedTypeSymbol?> FindRoslynNamedTypeSymbolAsync(string fullyQualifiedTypeName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Type?> FindReflectionTypeAsync(string fullyQualifiedTypeName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IEnumerable<Type>> SearchReflectionTypesAsync(string regexPattern, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IEnumerable<Project> GetProjects() => solution.Projects;
        public Project? GetProjectByName(string projectName) => solution.Projects.FirstOrDefault(p => p.Name == projectName);
        public Task<SemanticModel?> GetSemanticModelAsync(DocumentId documentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Compilation?> GetCompilationAsync(ProjectId projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReloadSolutionFromDiskAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void RefreshCurrentSolution() { }
        public void Dispose() { }
    }
}
