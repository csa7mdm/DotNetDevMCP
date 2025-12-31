// Copyright (c) 2025 Ahmed Mustafa

namespace DotNetDevMCP.Core.Interfaces;

/// <summary>
/// Service for advanced Git operations (Level C - Deep Integration)
/// </summary>
public interface ISourceControlService
{
    /// <summary>
    /// Gets the current repository status
    /// </summary>
    Task<RepositoryStatus> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes merge conflicts and provides resolution strategies
    /// </summary>
    Task<MergeAnalysis> AnalyzeMergeAsync(string repositoryPath, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs automated code review on changes
    /// </summary>
    Task<CodeReviewResult> ReviewChangesAsync(string repositoryPath, string? baseBranch = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes commit history and provides insights
    /// </summary>
    Task<HistoryAnalysis> AnalyzeHistoryAsync(string repositoryPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provides branch strategy recommendations
    /// </summary>
    Task<BranchStrategyRecommendation> AnalyzeBranchStrategyAsync(string repositoryPath, CancellationToken cancellationToken = default);
}

public record RepositoryStatus(
    int ModifiedFiles,
    int AddedFiles,
    int DeletedFiles,
    int UntrackedFiles,
    string CurrentBranch,
    bool IsDirty);

public record MergeAnalysis(
    int ConflictCount,
    IEnumerable<ConflictInfo> Conflicts,
    IEnumerable<string> ResolutionStrategies);

public record ConflictInfo(
    string FilePath,
    string ConflictType,
    int LineCount,
    string Suggestion);

public record CodeReviewResult(
    int FilesChanged,
    IEnumerable<ReviewComment> Comments,
    string OverallAssessment,
    IEnumerable<string> AffectedTests);

public record ReviewComment(
    string FilePath,
    int LineNumber,
    string Severity,
    string Message,
    string Category);

public record HistoryAnalysis(
    int TotalCommits,
    IEnumerable<CommitImpact> CommitImpacts,
    IEnumerable<string> FrequentContributors);

public record CommitImpact(
    string CommitHash,
    string Message,
    DateTime Date,
    string Author,
    int FilesChanged,
    int LinesAdded,
    int LinesDeleted);

public record BranchStrategyRecommendation(
    string CurrentStrategy,
    IEnumerable<string> Recommendations,
    IEnumerable<string> StaleBranches);
