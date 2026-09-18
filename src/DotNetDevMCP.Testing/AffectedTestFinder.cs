// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.CodeIntelligence.Interfaces;
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

    /// <param name="maxDepth">Reference hops to follow from a changed symbol. 1 = tests that call the changed code directly.</param>
    public async Task<IReadOnlyList<AffectedTest>> FindAsync(IEnumerable<string> changedFiles, int maxDepth, CancellationToken ct)
    {
        var solution = solutions.CurrentSolution ?? throw new InvalidOperationException("No solution is loaded. Call SharpTool_LoadSolution or start with --load-solution.");

        var found = new Dictionary<string, AffectedTest>(StringComparer.Ordinal);
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var frontier = new List<(ISymbol Symbol, string Via)>();

        foreach (var file in changedFiles)
        {
            var full = Path.GetFullPath(file);
            foreach (var docId in solution.GetDocumentIdsWithFilePath(full))
            {
                var doc = solution.GetDocument(docId);
                if (doc is null) continue;
                var model = await doc.GetSemanticModelAsync(ct);
                var root = await doc.GetSyntaxRootAsync(ct);
                if (model is null || root is null) continue;

                foreach (var decl in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
                {
                    // Fields: declared symbols live on the variables. Namespaces: references are every `using` in the solution.
                    if (decl is FieldDeclarationSyntax or EventFieldDeclarationSyntax or BaseNamespaceDeclarationSyntax) continue;
                    var symbol = model.GetDeclaredSymbol(decl, ct);
                    if (symbol is null || !seen.Add(symbol)) continue;
                    // A private member is only reachable through its type's non-private surface, which is also in this file.
                    if (symbol.DeclaredAccessibility == Accessibility.Private && symbol is not INamedTypeSymbol) continue;
                    var via = Path.GetFileName(full);
                    if (symbol is IMethodSymbol m && IsTestMethod(m))
                    {
                        Add(found, m, via, doc.Project.FilePath);
                    }
                    else
                    {
                        frontier.Add((symbol, via));
                    }
                }
            }
        }

        for (var depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<(ISymbol, string)>();
            // FindReferences is the cost; the workspace is safe to read concurrently, but unbounded fan-out thrashes.
            using var gate = new SemaphoreSlim(Environment.ProcessorCount);
            var refsBySymbol = await Task.WhenAll(frontier.Select(async f =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var refs = await SymbolFinder.FindReferencesAsync(f.Symbol, solution, ct);
                    logger.LogDebug("FindReferences {Symbol} ({Kind}): {Count} locations in {Ms} ms", f.Symbol.ToDisplayString(), f.Symbol.Kind, refs.Sum(r => r.Locations.Count()), sw.ElapsedMilliseconds);
                    return (f.Symbol, f.Via, Refs: refs);
                }
                finally { gate.Release(); }
            }));
            logger.LogDebug("Depth {Depth}: {Symbols} symbols searched", depth + 1, frontier.Count);
            foreach (var (symbol, via, refs) in refsBySymbol)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var location in refs.SelectMany(r => r.Locations))
                {
                    var enclosing = await EnclosingMemberAsync(location, ct);
                    if (enclosing is null || !seen.Add(enclosing)) continue;
                    if (enclosing.DeclaredAccessibility == Accessibility.Private && enclosing is not INamedTypeSymbol)
                    {
                        enclosing = enclosing.ContainingType; // hop through the type's public surface instead
                        if (enclosing is null || !seen.Add(enclosing)) continue;
                    }
                    var chain = $"{via} -> {symbol.Name}";
                    if (enclosing is IMethodSymbol m && IsTestMethod(m))
                    {
                        Add(found, m, chain, location.Document.Project.FilePath);
                    }
                    else
                    {
                        next.Add((enclosing, chain));
                    }
                }
            }
            frontier = next;
        }

        return found.Values.OrderBy(t => t.ProjectPath).ThenBy(t => t.FullyQualifiedName).ToList();
    }

    private static void Add(Dictionary<string, AffectedTest> found, IMethodSymbol test, string via, string? projectPath)
    {
        var fqn = FullyQualifiedName(test);
        found.TryAdd(fqn, new AffectedTest(fqn, projectPath ?? "", via));
    }

    private static async Task<ISymbol?> EnclosingMemberAsync(ReferenceLocation location, CancellationToken ct)
    {
        var root = await location.Document.GetSyntaxRootAsync(ct);
        var model = await location.Document.GetSemanticModelAsync(ct);
        if (root is null || model is null) return null;
        var node = root.FindNode(location.Location.SourceSpan);
        var member = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(m => m is not (FieldDeclarationSyntax or EventFieldDeclarationSyntax or BaseNamespaceDeclarationSyntax));
        if (member is null) return null;
        var symbol = model.GetDeclaredSymbol(member, ct);
        // A reference inside a property/ctor/field initializer: treat the containing type as the next hop.
        return symbol is IMethodSymbol or INamedTypeSymbol ? symbol : symbol?.ContainingType;
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
