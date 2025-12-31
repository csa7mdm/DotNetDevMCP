namespace SharpTools.Tools.Interfaces;

/// <summary>
/// Result of a merge analysis operation
/// </summary>
public record MergeAnalysisResult(
    bool CanMerge,
    bool HasConflicts,
    IReadOnlyList<string> ConflictingFiles,
    string AnalysisSummary
);

/// <summary>
/// Result of a code review operation
/// </summary>
public record CodeReviewResult(
    int FilesChanged,
    int LinesAdded,
    int LinesRemoved,
    IReadOnlyList<string> ModifiedFiles,
    string ReviewSummary
);

public interface IGitService {
    Task<bool> IsRepositoryAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task<bool> IsOnSharpToolsBranchAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task EnsureSharpToolsBranchAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task CommitChangesAsync(string solutionPath, IEnumerable<string> changedFilePaths, string commitMessage, CancellationToken cancellationToken = default);
    Task<(bool success, string diff)> RevertLastCommitAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task<string> GetBranchOriginCommitAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task<string> CreateUndoBranchAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task<string> GetDiffAsync(string solutionPath, string oldCommitSha, string newCommitSha, CancellationToken cancellationToken = default);

    // Advanced features
    Task<MergeAnalysisResult> AnalyzeMergeAsync(string solutionPath, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default);
    Task<CodeReviewResult> ReviewChangesAsync(string solutionPath, string baseBranch, string compareBranch, CancellationToken cancellationToken = default);
}