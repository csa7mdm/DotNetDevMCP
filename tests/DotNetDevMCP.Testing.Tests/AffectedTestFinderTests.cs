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
