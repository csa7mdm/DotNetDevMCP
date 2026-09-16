namespace DotNetDevMCP.Analysis.Models;

/// <summary>
/// Represents the result of a code analysis operation
/// </summary>
public sealed record AnalysisResult(
    string ProjectPath,
    int TotalFiles,
    int TotalLines,
    int CodeLines,
    int CommentLines,
    int BlankLines,
    int Classes,
    int Methods,
    double MaintainabilityIndex,
    double CyclomaticComplexity,
    DateTime AnalyzedAt
);

/// <summary>
/// Represents code quality metrics for a project
/// </summary>
public sealed record CodeQualityMetrics(
    string ProjectPath,
    double TechnicalDebtHours,
    int CodeSmells,
    int Bugs,
    int Vulnerabilities,
    double Coverage,
    double Duplication,
    IReadOnlyList<CodeIssue> Issues,
    DateTime AnalyzedAt
);

/// <summary>
/// Represents a single code issue
/// </summary>
public sealed record CodeIssue(
    string RuleId,
    string Message,
    string Severity,
    string FilePath,
    int LineNumber,
    int ColumnNumber,
    string? Suggestion = null
);

/// <summary>
/// Represents dependency information for a project
/// </summary>
public sealed record DependencyInfo(
    string ProjectPath,
    IReadOnlyList<PackageDependency> PackageDependencies,
    IReadOnlyList<ProjectReference> ProjectReferences,
    IReadOnlyList<string> FrameworkReferences,
    bool HasCircularDependencies,
    DateTime AnalyzedAt
);

/// <summary>
/// Represents a NuGet package dependency
/// </summary>
public sealed record PackageDependency(
    string Name,
    string Version,
    bool IsDirect,
    bool IsDevelopmentDependency,
    string? LicenseUrl = null,
    bool? HasKnownVulnerabilities = null
);

/// <summary>
/// Represents a project reference
/// </summary>
public sealed record ProjectReference(
    string Name,
    string Path,
    bool IsProjectReference
);

/// <summary>
/// Request model for code analysis
/// </summary>
public sealed record AnalysisRequest(
    string Path,
    bool IncludeMetrics = true,
    bool IncludeDependencies = true,
    bool IncludeIssues = true,
    string? Configuration = null,
    string? Platform = null
);
