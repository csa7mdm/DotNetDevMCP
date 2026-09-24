// Copyright (c) 2025 Ahmed Mustafa

using System.Collections.Immutable;
using DotNetDevMCP.CodeIntelligence.Interfaces;
using DotNetDevMCP.Core;
using DotNetDevMCP.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace DotNetDevMCP.Testing;

/// <summary>
/// Given changed source files, walks Roslyn references from the symbols they declare
/// until it reaches methods marked with a test attribute. Those are the tests to run.
/// </summary>
public sealed class AffectedTestFinder(ISolutionManager solutions, ILogger<AffectedTestFinder> logger)
{
    private static readonly HashSet<string> TestAttributes = new(StringComparer.Ordinal)
    {
        "Fact", "Theory", "Test", "TestCase", "TestCaseSource", "TestMethod", "DataTestMethod",
    };

    /// <summary>
    /// Default time allowed for tracing. The cost is the reference count of what changed, not the symbol count: one busy type
    /// (Outcome&lt;T&gt;, a context object) can have thousands of references. Past the budget, running everything is cheaper.
    /// </summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(10); // Polly: every selection slower than 10 s reached 467+ tests, where a full run is cheaper

    /// <param name="maxDepth">Reference hops to follow from a changed symbol. 1 = tests that call the changed code directly.</param>
    public Task<AffectedTestSelection> FindAsync(IEnumerable<string> changedFiles, int maxDepth, TimeSpan budget, CancellationToken ct) =>
        FindAsync(solutions.CurrentSolution ?? throw new InvalidOperationException("No solution is loaded. Call SharpTool_LoadSolution or start with --load-solution."),
            changedFiles, maxDepth, budget, logger, ct);

    public static async Task<AffectedTestSelection> FindAsync(Solution solution, IEnumerable<string> changedFiles, int maxDepth, TimeSpan budget, ILogger logger, CancellationToken callerCt)
    {
        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
        budgetCts.CancelAfter(budget);
        var ct = budgetCts.Token;

        // Multi-targeted projects load once per TFM ("Polly.Core(net8.0)", "(net9.0)", ...). Searching all of them repeats every hop
        // per TFM and the repeats compound per hop. Search one variant of each test project plus the projects it references.
        var scope = SearchScope(solution);
        var documents = scope.SelectMany(p => p.Documents).ToImmutableHashSet<Document>();
        // Denominator for "is this selection worth filtering for": syntax-only and outside the search budget, so it can't blow it.
        var totalTestMethods = await CountTestMethodsAsync(scope.Where(IsTestProject), callerCt);

        var found = new Dictionary<string, AffectedTest>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal); // by documentation id: the same method from another TFM is the same method
        var frontier = new List<(ISymbol Symbol, string Via)>();

        void Visit(ISymbol symbol, string via, string? projectPath, List<(ISymbol, string)> into)
        {
            if (!seen.Add(Key(symbol))) return;
            if (symbol is IMethodSymbol m && IsTestMethod(m)) Add(found, m, via, projectPath);
            else into.Add((symbol, via));
        }

        var searched = 0;
        var complete = true;
        try
        {
            foreach (var file in changedFiles)
            {
                var full = Path.GetFullPath(file);
                var ids = solution.GetDocumentIdsWithFilePath(full);
                var doc = solution.GetDocument(ids.FirstOrDefault(id => scope.Any(p => p.Id == id.ProjectId)) ?? ids.FirstOrDefault()!);
                if (doc is null) continue;
                var model = await doc.GetSemanticModelAsync(ct);
                var root = await doc.GetSyntaxRootAsync(ct);
                if (model is null || root is null) continue;

                foreach (var decl in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
                {
                    foreach (var symbol in RunnableSymbols(decl, model, ct))
                    {
                        Visit(symbol, Path.GetFileName(full), doc.Project.FilePath, frontier);
                    }
                }
            }

            for (var depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
            {
                searched += frontier.Count;
                var next = new List<(ISymbol, string)>();
                // FindReferences is the cost; the workspace is safe to read concurrently, but unbounded fan-out thrashes.
                using var gate = new SemaphoreSlim(Environment.ProcessorCount);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var refsBySymbol = await Task.WhenAll(frontier.Select(async f =>
                {
                    await gate.WaitAsync(ct);
                    try { return (f.Symbol, f.Via, Refs: await SymbolFinder.FindReferencesAsync(f.Symbol, solution, documents, ct)); }
                    finally { gate.Release(); }
                }));
                logger.LogDebug("Depth {Depth}: {Symbols} symbols searched in {Ms} ms", depth + 1, frontier.Count, sw.ElapsedMilliseconds);
                foreach (var (symbol, via, refs) in refsBySymbol)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var location in refs.SelectMany(r => r.Locations))
                    {
                        var root = await location.Document.GetSyntaxRootAsync(ct);
                        var model = await location.Document.GetSemanticModelAsync(ct);
                        var member = root?.FindNode(location.Location.SourceSpan).AncestorsAndSelf().OfType<MemberDeclarationSyntax>()
                            .FirstOrDefault(m => m is not BaseNamespaceDeclarationSyntax);
                        if (member is null || model is null) continue;
                        foreach (var enclosing in RunnableSymbols(member, model, ct))
                        {
                            Visit(enclosing, $"{via} -> {symbol.Name}", location.Document.Project.FilePath, next);
                        }
                    }
                }
                frontier = next;
            }
        }
        catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
        {
            // Out of budget. What was found is a partial set; reporting that is safe, silently returning it is not.
            logger.LogInformation("Affected-test walk out of its {Budget}s budget after {Symbols} symbols", budget.TotalSeconds, searched);
            complete = false;
        }

        var tests = found.Values.OrderBy(t => t.ProjectPath).ThenBy(t => t.FullyQualifiedName).ToList();
        return new AffectedTestSelection(tests, complete, searched, totalTestMethods);
    }

    /// <summary>
    /// Total test methods across the search scope's test projects, counted syntactically (no semantic model, no reference
    /// walk) so it's cheap regardless of solution size: one pass over each test project's syntax trees.
    /// </summary>
    private static async Task<int> CountTestMethodsAsync(IEnumerable<Project> testProjects, CancellationToken ct)
    {
        var total = 0;
        foreach (var project in testProjects)
        {
            foreach (var doc in project.Documents)
            {
                if (await doc.GetSyntaxRootAsync(ct) is not { } root) continue;
                total += root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count(HasTestAttributeSyntax);
            }
        }
        return total;
    }

    private static bool HasTestAttributeSyntax(MethodDeclarationSyntax method) =>
        method.AttributeLists.SelectMany(al => al.Attributes).Any(a => TestAttributes.Contains(AttributeShortName(a.Name.ToString())));

    /// <summary>"Xunit.Fact" or "FactAttribute" -&gt; "Fact". Syntax-only stand-in for <see cref="IsTestMethod"/>, which needs a symbol.</summary>
    private static string AttributeShortName(string name)
    {
        var dot = name.LastIndexOf('.');
        var simple = dot >= 0 ? name[(dot + 1)..] : name;
        return simple.EndsWith("Attribute", StringComparison.Ordinal) ? simple[..^9] : simple;
    }

    /// <summary>
    /// The code a declaration runs as. A type or a field runs through its constructors (initializers execute there),
    /// so hop through those, never through the type itself: every mention of a busy type would pull in half the solution.
    /// </summary>
    private static IEnumerable<ISymbol> RunnableSymbols(MemberDeclarationSyntax decl, SemanticModel model, CancellationToken ct)
    {
        if (decl is BaseFieldDeclarationSyntax field)
        {
            // The field itself too: readers see what the initializer built. xUnit [MemberData(nameof(Data))] reaches a static
            // data field only this way, since nothing references the static constructor its initializer runs in.
            var fields = field.Declaration.Variables.Select(v => model.GetDeclaredSymbol(v, ct)).OfType<ISymbol>().ToList();
            return fields.Count == 0 ? [] : fields.Concat(Constructors(fields[0].ContainingType));
        }
        return model.GetDeclaredSymbol(decl, ct) switch
        {
            INamedTypeSymbol type => Constructors(type),
            { } s when s is IMethodSymbol or IPropertySymbol or IEventSymbol or IFieldSymbol /* enum member */ => [s],
            _ => [],
        };
    }

    private static IEnumerable<ISymbol> Constructors(INamedTypeSymbol type) => type.InstanceConstructors.Concat(type.StaticConstructors);

    private static string Key(ISymbol s) => s.OriginalDefinition.GetDocumentationCommentId() ?? s.OriginalDefinition.ToDisplayString();

    private static List<Project> SearchScope(Solution solution)
    {
        var scope = new Dictionary<ProjectId, Project>();
        void AddWithReferences(Project p)
        {
            if (!scope.TryAdd(p.Id, p)) return;
            foreach (var r in p.ProjectReferences) if (solution.GetProject(r.ProjectId) is { } rp) AddWithReferences(rp);
        }
        // One variant per test project file: the highest TFM, compared as a version ("net10.0" sorts before "net9.0" as text).
        foreach (var variants in solution.Projects.Where(IsTestProject).GroupBy(p => p.FilePath ?? p.Name))
        {
            AddWithReferences(variants.OrderByDescending(p => TfmVersion(p.Name)).First());
        }
        return scope.Count > 0 ? scope.Values.ToList() : solution.Projects.ToList();
    }

    private static bool IsTestProject(Project p) => p.MetadataReferences.Any(r =>
        Path.GetFileName(r.Display ?? "") is var f && (f.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) || f.StartsWith("nunit", StringComparison.OrdinalIgnoreCase)
            || f.StartsWith("Microsoft.VisualStudio.TestPlatform.TestFramework", StringComparison.OrdinalIgnoreCase) || f.StartsWith("TUnit", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Cheap fallback for when the reference walk in <see cref="FindAsync(Solution,IEnumerable{string},int,TimeSpan,ILogger,CancellationToken)"/>
    /// can't finish, or finishes with too large a selection: instead of tracing symbols, walk the project reference graph.
    /// Returns every test project that (transitively) references, via a Roslyn <see cref="ProjectReference"/>, a project
    /// containing one of the changed files - or contains one itself. This is a superset of the affected tests (reflection
    /// aside) and cheap, since it only looks at the project graph, not symbols or syntax.
    /// </summary>
    /// <remarks>
    /// Reachability is computed over every TFM variant of every project (unlike <see cref="SearchScope"/>, which keeps only
    /// the highest-TFM variant): a project's net8.0 variant can reference a different variant of a changed library than its
    /// net10.0 variant does, so dropping variants here could miss a real edge. The result is deduped to project FILE paths
    /// only at the very end, once every variant has had its say.
    /// ponytail: a test project that depends on the changed code only through a PackageReference (no ProjectReference) has no
    /// edge in this graph and will not be found. That's a real gap, not a bug - documenting it here and in the tool's
    /// response note is the fix, since detecting package-mediated dependencies would need a much heavier analysis.
    /// </remarks>
    public static IReadOnlyList<string> FindAffectedTestProjects(Solution solution, IEnumerable<string> changedFiles)
    {
        var changedProjectIds = changedFiles.SelectMany(f => OwningProjects(solution, f)).ToHashSet();
        if (changedProjectIds.Count == 0) return [];

        // Reverse ProjectReference edges (referenced -> referencing projects), across all TFM variants.
        var dependents = new Dictionary<ProjectId, List<ProjectId>>();
        foreach (var project in solution.Projects)
        {
            foreach (var reference in project.ProjectReferences)
            {
                if (!dependents.TryGetValue(reference.ProjectId, out var list)) dependents[reference.ProjectId] = list = [];
                list.Add(project.Id);
            }
        }

        var reachable = new HashSet<ProjectId>(changedProjectIds);
        var frontier = new Queue<ProjectId>(changedProjectIds);
        while (frontier.Count > 0)
        {
            if (!dependents.TryGetValue(frontier.Dequeue(), out var deps)) continue;
            foreach (var dep in deps) if (reachable.Add(dep)) frontier.Enqueue(dep);
        }

        return reachable.Select(solution.GetProject).OfType<Project>().Where(IsRunnableTestProject)
            .Select(p => p.FilePath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The projects a changed file belongs to: those that compile it, or else those whose folder holds it (the deepest such
    /// folder, for nested projects). That covers files the reference walk can't see: .csproj, .razor, .json, resources, and
    /// deleted files. Empty when the file is outside every project folder (Directory.Build.props, global.json).
    /// </summary>
    public static IReadOnlyList<ProjectId> OwningProjects(Solution solution, string file)
    {
        var full = Path.GetFullPath(file);
        var ids = solution.GetDocumentIdsWithFilePath(full);
        if (!ids.IsEmpty) return ids.Select(id => id.ProjectId).Distinct().ToList();

        var owners = solution.Projects
            .Select(p => (p.Id, Dir: Path.GetDirectoryName(p.FilePath)))
            .Where(p => p.Dir != null && PathBoundary.IsWithin(full, p.Dir))
            .ToList();
        if (owners.Count == 0) return [];
        var deepest = owners.Max(p => p.Dir!.Length);
        return owners.Where(p => p.Dir!.Length == deepest).Select(p => p.Id).ToList();
    }

    /// <summary>
    /// A project `dotnet test` can run: references a test framework AND declares a test method. Helper libraries such as
    /// Polly.TestUtils reference xUnit without containing tests, and `dotnet test` on them fails with "No test projects were found".
    /// Syntax only, so cheap; a syntax tree already parsed is cached by the workspace.
    /// </summary>
    private static bool IsRunnableTestProject(Project p) =>
        // ponytail: synchronous parse; the server has no synchronization context and parsing is cheap next to the build that follows.
        IsTestProject(p) && p.Documents.Any(d => d.GetSyntaxRootAsync().GetAwaiter().GetResult() is { } root
            && root.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(HasTestAttributeSyntax));

    /// <summary>Every test project in the solution, deduped by file path across TFM variants. Denominator for deciding
    /// whether <see cref="FindAffectedTestProjects"/> reached "basically everything", where running the whole solution
    /// in one invocation is simpler than filtering to a selection that isn't actually smaller.</summary>
    public static IReadOnlyList<string> AllTestProjectFilePaths(Solution solution) =>
        solution.Projects.Where(IsRunnableTestProject).Select(p => p.FilePath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>"Polly.Core.Tests(net10.0)" -> 10.0; no TFM suffix -> 0.</summary>
    private static Version TfmVersion(string projectName)
    {
        var open = projectName.LastIndexOf("(net", StringComparison.Ordinal);
        if (open < 0) return new Version(0, 0);
        var digits = new string(projectName[(open + 4)..].TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return Version.TryParse(digits.Contains('.') ? digits : digits + ".0", out var v) ? v : new Version(0, 0);
    }

    private static void Add(Dictionary<string, AffectedTest> found, IMethodSymbol test, string via, string? projectPath)
    {
        var fqn = FullyQualifiedName(test);
        found.TryAdd(fqn, new AffectedTest(fqn, projectPath ?? "", via));
    }

    private static bool IsTestMethod(IMethodSymbol m) =>
        m.GetAttributes().Any(a => a.AttributeClass is { } c && TestAttributes.Contains(c.Name.EndsWith("Attribute", StringComparison.Ordinal) ? c.Name[..^9] : c.Name));

    /// <summary>VSTest FullyQualifiedName form: Namespace.Outer+Nested.Method</summary>
    private static string FullyQualifiedName(IMethodSymbol m)
    {
        var types = new Stack<string>();
        for (var t = m.ContainingType; t is not null; t = t.ContainingType) types.Push(t.MetadataName);
        var ns = m.ContainingNamespace.IsGlobalNamespace ? "" : m.ContainingNamespace.ToDisplayString() + ".";
        return $"{ns}{string.Join("+", types)}.{m.Name}";
    }
}
