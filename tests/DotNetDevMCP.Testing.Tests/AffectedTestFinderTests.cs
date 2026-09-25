using DotNetDevMCP.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetDevMCP.Testing.Tests;

public class AffectedTestFinderTests
{
    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "repo");

    [Fact]
    public async Task Follows_field_initializers_and_private_members_and_reports_each_test_once_across_tfms()
    {
        var solution = BuildSolution(
            lib: new()
            {
                ["A.cs"] = "namespace Lib; public class A { public int M() => 1; }",
                ["B.cs"] = "namespace Lib; public class B { private readonly int _x = new A().M(); public int N() => _x; }",
                ["C.cs"] = "namespace Lib; public static class C { private static int Hidden() => new A().M(); public static int ViaPrivate() => Hidden(); }",
            },
            tests: """
                using Xunit;
                namespace Lib.Tests;
                public class LibTests
                {
                    [Fact] public void Through_field_initializer() { _ = new Lib.B().N(); }
                    [Fact] public void Through_private_member() { _ = Lib.C.ViaPrivate(); }
                    [Fact] public void Unrelated() { }
                }
                """);

        var affected = await AffectedTestFinder.FindAsync(solution, [Path.Combine(Root, "src", "A.cs")], maxDepth: 3, AffectedTestFinder.DefaultBudget, NullLogger.Instance, default);

        Assert.True(affected.Complete);
        Assert.Equal(
            ["Lib.Tests.LibTests.Through_field_initializer", "Lib.Tests.LibTests.Through_private_member"],
            affected.Tests.Select(t => t.FullyQualifiedName).Order());
    }

    [Fact]
    public async Task Finds_theories_whose_member_data_field_calls_the_changed_code()
    {
        var solution = BuildSolution(
            lib: new() { ["A.cs"] = "namespace Lib; public class A { public int M() => 1; }" },
            tests: """
                using Xunit;
                namespace Lib.Tests;
                public class LibTests
                {
                    public static readonly TheoryData<int> Data = new() { new Lib.A().M() };
                    [Theory, MemberData(nameof(Data))] public void Uses_data(int x) { }
                    [Fact] public void Unrelated() { }
                }
                """);

        var affected = await AffectedTestFinder.FindAsync(solution, [Path.Combine(Root, "src", "A.cs")], maxDepth: 8, AffectedTestFinder.DefaultBudget, NullLogger.Instance, default);

        Assert.Equal(["Lib.Tests.LibTests.Uses_data"], affected.Tests.Select(t => t.FullyQualifiedName));
    }

    [Fact]
    public async Task Counts_test_methods_in_scope_and_not_other_methods()
    {
        var solution = BuildSolution(
            lib: new() { ["A.cs"] = "namespace Lib; public class A { public int M() => 1; public int Helper() => 2; }" },
            tests: """
                using Xunit;
                namespace Lib.Tests;
                public class LibTests
                {
                    [Fact] public void One() { }
                    [Fact] public void Two() { }
                    [Theory] public void Three(int x) { }
                    private void NotATest() { }
                    public void AlsoNotATest() { }
                }
                """);

        // Both TFM variants of Lib.Tests share the same file, so the scope counts LibTests.cs once: 3 test methods, not 6.
        var affected = await AffectedTestFinder.FindAsync(solution, [Path.Combine(Root, "src", "A.cs")], maxDepth: 1, AffectedTestFinder.DefaultBudget, NullLogger.Instance, default);

        Assert.Equal(3, affected.TotalTestMethods);
    }

    [Fact]
    public async Task Reports_an_incomplete_selection_instead_of_dropping_tests_when_the_budget_runs_out()
    {
        var solution = BuildSolution(
            lib: new() { ["A.cs"] = "namespace Lib; public class A { public int M() => 1; }", ["B.cs"] = "namespace Lib; public class B { public int N() => new A().M(); }" },
            tests: "using Xunit; namespace Lib.Tests; public class LibTests { [Fact] public void Deep() { _ = new Lib.B().N(); } }");

        var affected = await AffectedTestFinder.FindAsync(solution, [Path.Combine(Root, "src", "A.cs")], maxDepth: 3, TimeSpan.Zero, NullLogger.Instance, default);

        Assert.False(affected.Complete);
    }

    [Fact]
    public async Task Finds_tests_that_call_the_changed_implementation_only_through_its_interface()
    {
        // The test never names Impl: it calls IService.Run on an instance it gets from somewhere else (DI, a mock setup).
        var solution = BuildSolution(
            lib: new()
            {
                ["IService.cs"] = "namespace Lib; public interface IService { int Run(); }",
                ["Impl.cs"] = "namespace Lib; public class Impl : IService { public int Run() => 1; }",
                ["Base.cs"] = "namespace Lib; public abstract class Base { public abstract int Go(); }",
                ["Derived.cs"] = "namespace Lib; public class Derived : Base { public override int Go() => 2; }",
            },
            tests: """
                using Xunit;
                namespace Lib.Tests;
                public class LibTests
                {
                    private readonly Lib.IService _service = null!;
                    private readonly Lib.Base _base = null!;
                    [Fact] public void Through_interface() { _ = _service.Run(); }
                    [Fact] public void Through_base_class() { _ = _base.Go(); }
                    [Fact] public void Unrelated() { }
                }
                """);

        var affected = await AffectedTestFinder.FindAsync(solution, [Path.Combine(Root, "src", "Impl.cs"), Path.Combine(Root, "src", "Derived.cs")],
            maxDepth: 3, AffectedTestFinder.DefaultBudget, NullLogger.Instance, default);

        Assert.Equal(
            ["Lib.Tests.LibTests.Through_base_class", "Lib.Tests.LibTests.Through_interface"],
            affected.Tests.Select(t => t.FullyQualifiedName).Order());
    }

    [Fact]
    public void Project_fallback_maps_a_file_the_solution_does_not_compile_to_the_project_folder_holding_it()
    {
        var solution = BuildSolution(lib: new() { ["A.cs"] = "namespace Lib; public class A { }" },
            tests: "using Xunit; namespace Lib.Tests; public class LibTests { [Fact] public void T() { } }");

        // appsettings.json, a .razor file, the .csproj itself, or a deleted .cs file: none is a document the walk can trace.
        var affected = AffectedTestFinder.FindAffectedTestProjects(solution, [Path.Combine(Root, "src", "appsettings.json")]);

        Assert.Equal([Path.Combine(Root, "test", "Lib.Tests.csproj")], affected);
        Assert.Empty(AffectedTestFinder.OwningProjects(solution, Path.Combine(Root, "Directory.Build.props")));
    }

    [Fact]
    public void Project_fallback_finds_only_the_test_project_that_references_the_changed_library()
    {
        var solution = BuildTwoLibrarySolution(out var libATests, out _);

        var affected = AffectedTestFinder.FindAffectedTestProjects(solution, [Path.Combine(Root, "src", "A.cs")]);

        Assert.Equal([libATests], affected);
    }

    [Fact]
    public void Project_fallback_includes_a_test_project_whose_own_file_changed()
    {
        var solution = BuildTwoLibrarySolution(out var libATests, out _);

        // LibA.Tests.cs belongs to the test project itself, not to any library it references.
        var affected = AffectedTestFinder.FindAffectedTestProjects(solution, [Path.Combine(Root, "test", "LibA.Tests.cs")]);

        Assert.Equal([libATests], affected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Selects_tests_of_every_in_scope_tfm_variant_of_a_changed_file(bool netStandardVariantFirst)
    {
        // Polly shape (RandomUtil): an internal class, seen through InternalsVisibleTo. Lib is multi-targeted, so its file exists once per TFM. Lib.Tests (net10.0) references Lib(net8.0);
        // Legacy.Tests reaches Lib(netstandard2.0) through Legacy. Both Lib variants are in the search scope. Seeding the walk
        // from whichever variant comes first found only that variant's dependents, and the order isn't stable.
        var workspace = new AdhocWorkspace();
        var corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var runtime = MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"));
        var xunit = MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location);
        var solution = workspace.CurrentSolution;
        var libFile = Path.Combine(Root, "src", "Util.cs");
        // Declarations sit on different lines per #if branch, as in Polly's RandomUtil.cs (Next is on line 9 or line 16).
        const string libCode = """
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Lib.Tests")]
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Legacy")]
            namespace Lib;
            internal static class Util
            {
            #if NET
                public static int N() => 1;
            #else
                private static readonly int Seed = 2;

                public static int N() => Seed;
            #endif
            }
            """;

        ProjectId AddLib(string tfm)
        {
            var id = ProjectId.CreateNewId();
            solution = solution
                .AddProject(ProjectInfo.Create(id, VersionStamp.Default, $"Lib({tfm})", "Lib", LanguageNames.CSharp,
                    filePath: Path.Combine(Root, "src", "Lib.csproj"), metadataReferences: [corlib, runtime],
                    parseOptions: new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(preprocessorSymbols: tfm.StartsWith("net8") ? ["NET"] : [])))
                .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(id), "Util.cs",
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(libCode), VersionStamp.Default)), filePath: libFile));
            return id;
        }
        ProjectId AddProject(string name, string tfm, string dir, string code, ProjectId reference, bool isTest)
        {
            var id = ProjectId.CreateNewId();
            solution = solution
                .AddProject(ProjectInfo.Create(id, VersionStamp.Default, $"{name}({tfm})", name, LanguageNames.CSharp,
                    filePath: Path.Combine(Root, dir, $"{name}.csproj"), metadataReferences: isTest ? [corlib, runtime, xunit] : [corlib, runtime]))
                .AddProjectReference(id, new ProjectReference(reference))
                .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(id), $"{name}.cs",
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(code), VersionStamp.Default)), filePath: Path.Combine(Root, dir, $"{name}.cs")));
            return id;
        }

        ProjectId libNet8, libNs20;
        if (netStandardVariantFirst) { libNs20 = AddLib("netstandard2.0"); libNet8 = AddLib("net8.0"); }
        else { libNet8 = AddLib("net8.0"); libNs20 = AddLib("netstandard2.0"); }
        AddProject("Lib.Tests", "net10.0", "test", "using Xunit; namespace Lib.Tests; public class T { [Fact] public void Direct() { _ = Lib.Util.N(); } }", libNet8, isTest: true);
        var legacy = AddProject("Legacy", "netstandard2.0", "legacy", "namespace Legacy; public static class L { public static int M() => Lib.Util.N(); }", libNs20, isTest: false);
        AddProject("Legacy.Tests", "net10.0", "legacytest", "using Xunit; namespace Legacy.Tests; public class T { [Fact] public void ViaLegacy() { _ = Legacy.L.M(); } }", legacy, isTest: true);

        var affected = await AffectedTestFinder.FindAsync(solution, [libFile], maxDepth: 3, AffectedTestFinder.DefaultBudget, NullLogger.Instance, default);

        Assert.True(affected.Complete);
        Assert.Equal(["Legacy.Tests.T.ViaLegacy", "Lib.Tests.T.Direct"], affected.Tests.Select(t => t.FullyQualifiedName).Order());
    }

    [Fact]
    public void Project_fallback_walks_every_tfm_variant_of_the_reference_graph()
    {
        // LibA is multi-targeted, and its net8.0 variant carries a file the net10.0 variant doesn't (e.g. conditional
        // compilation). LibA.Tests is multi-targeted too, and each TFM variant references only the matching LibA variant -
        // so only the net8.0 edge leads from the changed file to LibA.Tests. Restricting the walk to one TFM (as
        // AffectedTestFinder.SearchScope does for the symbol-level search) would miss this; the project-level fallback must not.
        var workspace = new AdhocWorkspace();
        var corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var runtime = MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"));
        var xunit = MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location);
        var solution = workspace.CurrentSolution;

        var libA8 = ProjectId.CreateNewId();
        var libA10 = ProjectId.CreateNewId();
        var libAPath = Path.Combine(Root, "src", "LibA.csproj");
        var net8OnlyFile = Path.Combine(Root, "src", "A.net8.cs");
        solution = solution
            .AddProject(ProjectInfo.Create(libA8, VersionStamp.Default, "LibA(net8.0)", "LibA", LanguageNames.CSharp, filePath: libAPath, metadataReferences: [corlib, runtime]))
            .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(libA8), "A.net8.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From("namespace LibA; public class A8 { public int M() => 1; }"), VersionStamp.Default)), filePath: net8OnlyFile))
            .AddProject(ProjectInfo.Create(libA10, VersionStamp.Default, "LibA(net10.0)", "LibA", LanguageNames.CSharp, filePath: libAPath, metadataReferences: [corlib, runtime]));

        var testsPath = Path.Combine(Root, "test", "LibA.Tests.csproj");
        var testsFile = Path.Combine(Root, "test", "LibA.Tests.cs");
        const string testsCode = "using Xunit; namespace LibA.Tests; public class T { [Fact] public void T1() { } }";
        var tests8 = ProjectId.CreateNewId();
        var tests10 = ProjectId.CreateNewId();
        solution = solution
            .AddProject(ProjectInfo.Create(tests8, VersionStamp.Default, "LibA.Tests(net8.0)", "LibA.Tests", LanguageNames.CSharp, filePath: testsPath, metadataReferences: [corlib, runtime, xunit]))
            .AddProjectReference(tests8, new ProjectReference(libA8))
            .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(tests8), "LibA.Tests.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(testsCode), VersionStamp.Default)), filePath: testsFile))
            .AddProject(ProjectInfo.Create(tests10, VersionStamp.Default, "LibA.Tests(net10.0)", "LibA.Tests", LanguageNames.CSharp, filePath: testsPath, metadataReferences: [corlib, runtime, xunit]))
            .AddProjectReference(tests10, new ProjectReference(libA10))
            .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(tests10), "LibA.Tests.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(testsCode), VersionStamp.Default)), filePath: testsFile));

        // A helper library that references xUnit but declares no tests (like Polly.TestUtils): `dotnet test` can't run it.
        var utils = ProjectId.CreateNewId();
        solution = solution
            .AddProject(ProjectInfo.Create(utils, VersionStamp.Default, "LibA.TestUtils(net8.0)", "LibA.TestUtils", LanguageNames.CSharp,
                filePath: Path.Combine(Root, "test", "LibA.TestUtils.csproj"), metadataReferences: [corlib, runtime, xunit]))
            .AddProjectReference(utils, new ProjectReference(libA8))
            .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(utils), "Fakes.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From("namespace LibA.TestUtils; public static class Fakes { public static int One() => 1; }"), VersionStamp.Default)),
                filePath: Path.Combine(Root, "test", "Fakes.cs")));

        var affected = AffectedTestFinder.FindAffectedTestProjects(solution, [net8OnlyFile]);

        Assert.Equal([testsPath], affected);
    }

    [Fact]
    public void AllTestProjectFilePaths_dedupes_multi_tfm_test_projects_by_file_path()
    {
        var solution = BuildTwoLibrarySolution(out var libATests, out var libBTests);

        var all = AffectedTestFinder.AllTestProjectFilePaths(solution);

        Assert.Equal([libATests, libBTests], all.Order());
    }

    /// <summary>Two independent libraries, each with its own dual-TFM test project referencing only that library.</summary>
    private static Solution BuildTwoLibrarySolution(out string libATestsPath, out string libBTestsPath)
    {
        var workspace = new AdhocWorkspace();
        var corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var runtime = MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"));
        var xunit = MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location);
        var solution = workspace.CurrentSolution;

        ProjectId AddLib(string name, string file, string code)
        {
            var id = ProjectId.CreateNewId();
            solution = solution
                .AddProject(ProjectInfo.Create(id, VersionStamp.Default, name, name, LanguageNames.CSharp,
                    filePath: Path.Combine(Root, "src", $"{name}.csproj"), metadataReferences: [corlib, runtime]))
                .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(id), file,
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(code), VersionStamp.Default)), filePath: Path.Combine(Root, "src", file)));
            return id;
        }

        string AddTests(string name, ProjectId libId, string code)
        {
            var csprojPath = Path.Combine(Root, "test", $"{name}.csproj");
            foreach (var tfm in new[] { "net8.0", "net10.0" })
            {
                var id = ProjectId.CreateNewId();
                solution = solution
                    .AddProject(ProjectInfo.Create(id, VersionStamp.Default, $"{name}({tfm})", name, LanguageNames.CSharp,
                        filePath: csprojPath, metadataReferences: [corlib, runtime, xunit]))
                    .AddProjectReference(id, new ProjectReference(libId))
                    .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(id), $"{name}.cs",
                        loader: TextLoader.From(TextAndVersion.Create(SourceText.From(code), VersionStamp.Default)), filePath: Path.Combine(Root, "test", $"{name}.cs")));
            }
            return csprojPath;
        }

        var libAId = AddLib("LibA", "A.cs", "namespace LibA; public class A { public int M() => 1; }");
        var libBId = AddLib("LibB", "B.cs", "namespace LibB; public class B { public int M() => 1; }");

        libATestsPath = AddTests("LibA.Tests", libAId, "using Xunit; namespace LibA.Tests; public class T { [Fact] public void T1() { _ = new LibA.A().M(); } }");
        libBTestsPath = AddTests("LibB.Tests", libBId, "using Xunit; namespace LibB.Tests; public class T { [Fact] public void T1() { _ = new LibB.B().M(); } }");

        return solution;
    }

    /// <summary>A library and its test project, the test project loaded twice as MSBuildWorkspace does for two TFMs.</summary>
    private static Solution BuildSolution(Dictionary<string, string> lib, string tests)
    {
        var workspace = new AdhocWorkspace();
        var corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var runtime = MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"));
        var xunit = MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location);

        var libId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(libId, VersionStamp.Default, "Lib", "Lib", LanguageNames.CSharp,
            filePath: Path.Combine(Root, "src", "Lib.csproj"), metadataReferences: [corlib, runtime]));
        foreach (var (name, code) in lib)
        {
            solution = solution.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(libId), name,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(code), VersionStamp.Default)), filePath: Path.Combine(Root, "src", name)));
        }

        foreach (var tfm in new[] { "net8.0", "net10.0" })
        {
            var testId = ProjectId.CreateNewId();
            solution = solution.AddProject(ProjectInfo.Create(testId, VersionStamp.Default, $"Lib.Tests({tfm})", "Lib.Tests", LanguageNames.CSharp,
                    filePath: Path.Combine(Root, "test", "Lib.Tests.csproj"), metadataReferences: [corlib, runtime, xunit]))
                .AddProjectReference(testId, new ProjectReference(libId))
                .AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(testId), "LibTests.cs",
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(tests), VersionStamp.Default)), filePath: Path.Combine(Root, "test", "LibTests.cs")));
        }
        return solution;
    }
}
