using DotNetDevMCP.CodeIntelligence.Interfaces;
using DotNetDevMCP.CodeIntelligence.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetDevMCP.CodeIntelligence.Tests;

/// <summary>
/// Covers the fix for SharpTool_AnalyzeComplexity emitting empty "{}" method entries. The root cause
/// was AnalyzeMethodAsync casting the member's declaring syntax to MethodDeclarationSyntax, which only
/// ordinary methods satisfy - constructors, destructors, operators and property/event accessors all
/// failed that cast and returned an empty (and name-less) dictionary. These tests exercise
/// ComplexityAnalysisService directly against a bare Compilation, since the code under test never
/// needs a loaded solution/workspace unless it's computing cross-type coupling.
/// </summary>
public class ComplexityAnalysisEntriesTests {
    private static (Compilation compilation, SyntaxTree tree) BuildCompilation(string code) {
        var tree = CSharpSyntaxTree.ParseText(code);
        var references = new[] {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "ComplexityAnalysisEntriesTests.Compilation",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (compilation, tree);
    }

    [Fact]
    public async Task AnalyzeMethodAsync_populates_name_and_metrics_for_a_constructor() {
        var (compilation, tree) = BuildCompilation("""
            public class Widget {
                public Widget() { var x = 1; }
            }
            """);
        var root = await tree.GetRootAsync();
        var semanticModel = compilation.GetSemanticModel(tree);
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var typeSymbol = semanticModel.GetDeclaredSymbol(classDecl)!;
        var ctor = typeSymbol.GetMembers().OfType<IMethodSymbol>().Single(m => m.MethodKind == MethodKind.Constructor);

        var service = new ComplexityAnalysisService(new NoSolutionManager(), NullLogger<ComplexityAnalysisService>.Instance);
        var metrics = new Dictionary<string, object>();
        var recommendations = new List<string>();

        await service.AnalyzeMethodAsync(ctor, metrics, recommendations, CancellationToken.None);

        // Before the fix this dictionary came back completely empty for a constructor.
        Assert.NotEmpty(metrics);
        Assert.Equal(ctor.Name, metrics["name"]);
        Assert.True(metrics.ContainsKey("lineCount"));
        Assert.True(metrics.ContainsKey("cyclomaticComplexity"));
    }

    [Fact]
    public async Task AnalyzeMethodAsync_populates_name_for_an_ordinary_method_too() {
        // Regression guard for the second part of the bug: even methods that DID get metrics before
        // never carried their own name, making the JSON output useless to a reader.
        var (compilation, tree) = BuildCompilation("""
            public class Widget {
                public int DoWork(int a) => a * 2;
            }
            """);
        var root = await tree.GetRootAsync();
        var semanticModel = compilation.GetSemanticModel(tree);
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var typeSymbol = semanticModel.GetDeclaredSymbol(classDecl)!;
        var method = typeSymbol.GetMembers().OfType<IMethodSymbol>().Single(m => m.MethodKind == MethodKind.Ordinary);

        var service = new ComplexityAnalysisService(new NoSolutionManager(), NullLogger<ComplexityAnalysisService>.Instance);
        var metrics = new Dictionary<string, object>();

        await service.AnalyzeMethodAsync(method, metrics, new List<string>(), CancellationToken.None);

        Assert.Equal("DoWork", metrics["name"]);
    }

    [Fact]
    public async Task AnalyzeTypeAsync_methods_list_has_no_empty_entries_and_every_entry_is_named() {
        var (compilation, tree) = BuildCompilation("""
            public class Widget {
                public Widget() { }
                public int Value { get; set; }
                public int DoWork(int a) {
                    if (a > 0) { return a; }
                    return -a;
                }
            }
            """);
        var root = await tree.GetRootAsync();
        var semanticModel = compilation.GetSemanticModel(tree);
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var typeSymbol = semanticModel.GetDeclaredSymbol(classDecl)!;

        var service = new ComplexityAnalysisService(new NoSolutionManager(), NullLogger<ComplexityAnalysisService>.Instance);
        var metrics = new Dictionary<string, object>();

        await service.AnalyzeTypeAsync(typeSymbol, metrics, new List<string>(), includeGeneratedCode: false, CancellationToken.None);

        var typeMetrics = Assert.IsType<Dictionary<string, object>>(metrics["typeMetrics"]);
        var methods = Assert.IsType<List<Dictionary<string, object>>>(typeMetrics["methods"]);

        Assert.NotEmpty(methods);
        Assert.All(methods, m => Assert.True(m.Count > 0, "no method entry should serialize as an empty {}"));
        Assert.All(methods, m => Assert.True(m.ContainsKey("name"), "every method entry must carry its name"));

        // The constructor and the auto-property's accessors used to disappear into empty "{}" entries -
        // they must now show up like any other method, each with a distinguishing name.
        Assert.Contains(methods, m => (string)m["name"] == ".ctor");
        Assert.Contains(methods, m => (string)m["name"] == "DoWork");
    }

    /// <summary>
    /// ComplexityAnalysisService only touches ISolutionManager.CurrentSolution when computing
    /// cross-type coupling; name/metric computation for a single method or type never needs a loaded
    /// solution, so a bare Compilation is enough and this stand-in can just report "nothing loaded".
    /// </summary>
    private sealed class NoSolutionManager : ISolutionManager {
        public bool IsSolutionLoaded => false;
        public Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace? CurrentWorkspace => null;
        public Solution? CurrentSolution => null;

        public Task LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken) => Task.CompletedTask;
        public void UnloadSolution() { }
        public Task<ISymbol?> FindRoslynSymbolAsync(string fullyQualifiedName, CancellationToken cancellationToken) => Task.FromResult<ISymbol?>(null);
        public Task<INamedTypeSymbol?> FindRoslynNamedTypeSymbolAsync(string fullyQualifiedTypeName, CancellationToken cancellationToken) => Task.FromResult<INamedTypeSymbol?>(null);
        public Task<Type?> FindReflectionTypeAsync(string fullyQualifiedTypeName, CancellationToken cancellationToken) => Task.FromResult<Type?>(null);
        public Task<IEnumerable<Type>> SearchReflectionTypesAsync(string regexPattern, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Type>());
        public IEnumerable<Project> GetProjects() => Enumerable.Empty<Project>();
        public Project? GetProjectByName(string projectName) => null;
        public Task<SemanticModel?> GetSemanticModelAsync(DocumentId documentId, CancellationToken cancellationToken) => Task.FromResult<SemanticModel?>(null);
        public Task<Compilation?> GetCompilationAsync(ProjectId projectId, CancellationToken cancellationToken) => Task.FromResult<Compilation?>(null);
        public Task ReloadSolutionFromDiskAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void RefreshCurrentSolution() { }
        public void Dispose() { }
    }
}
