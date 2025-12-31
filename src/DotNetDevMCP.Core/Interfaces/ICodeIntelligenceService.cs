// Copyright (c) 2025 Ahmed Mustafa

using Microsoft.CodeAnalysis;

namespace DotNetDevMCP.Core.Interfaces;

/// <summary>
/// Service for Roslyn-based code intelligence operations
/// </summary>
public interface ICodeIntelligenceService
{
    /// <summary>
    /// Loads a solution from disk
    /// </summary>
    Task<Solution?> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a symbol by its fully qualified name
    /// </summary>
    Task<ISymbol?> FindSymbolAsync(Solution solution, string fullyQualifiedName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all references to a symbol
    /// </summary>
    Task<IEnumerable<Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation>> FindReferencesAsync(Solution solution, ISymbol symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes code complexity for a symbol
    /// </summary>
    Task<ComplexityMetrics> AnalyzeComplexityAsync(ISymbol symbol, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents code complexity metrics
/// </summary>
public record ComplexityMetrics(
    int CyclomaticComplexity,
    int CognitiveComplexity,
    int LinesOfCode,
    string Recommendation);
