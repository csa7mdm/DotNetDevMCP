// Copyright (c) 2025 Ahmed Mustafa

using ModelContextProtocol;
using DotNetDevMCP.SourceControl.Services;

namespace DotNetDevMCP.SourceControl.Mcp.Tools;

/// <summary>
/// Marker class for ILogger category specific to SourceControlTools
/// </summary>
public class SourceControlToolsLogCategory { }

/// <summary>
/// MCP Tools for git source control operations
/// </summary>
[McpServerToolType]
public static partial class SourceControlTools
{
    [McpServerTool(Name = "git_repo_status", Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets comprehensive information about a git repository including current branch, changes, ahead/behind status, and remotes.")]
    public static async Task<object> GetRepositoryStatus(
        GitService gitService,
        ILogger<SourceControlToolsLogCategory> logger,
        [Description("Path to the git repository root or any subdirectory")] string repoPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Getting repository status for: {RepoPath}", repoPath);
            
            var info = await gitService.GetRepoInfoAsync(repoPath, cancellationToken);
            
            return new
            {
                Success = true,
                RepositoryRoot = info.RootPath,
                CurrentBranch = info.CurrentBranch,
                IsDirty = info.IsDirty,
                AheadOfRemote = info.AheadCount,
                BehindRemote = info.BehindCount,
                Remotes = info.Remotes,
                UntrackedFiles = info.UntrackedFiles.Take(20),
                ChangedFiles = info.Changes.Select(c => new
                {
                    c.FilePath,
                    Status = c.Status.ToString()
                }).Take(50),
                TotalChanges = info.Changes.Count(),
                TotalUntracked = info.UntrackedFiles.Count()
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get repository status");
            return new 
            { 
                Success = false, 
                Error = ex.Message,
                IsGitRepository = false
            };
        }
    }

    [McpServerTool(Name = "git_list_branches", Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lists all branches in a git repository, with option to include remote branches.")]
    public static async Task<object> ListBranches(
        GitService gitService,
        ILogger<SourceControlToolsLogCategory> logger,
        [Description("Path to the git repository")] string repoPath,
        [Description("Include remote branches in the listing")] bool includeRemote = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Listing branches for: {RepoPath}", repoPath);
            
            var branches = await gitService.GetBranchesAsync(repoPath, includeRemote, cancellationToken);
            var currentBranch = await gitService.GetCurrentBranchAsync(repoPath, cancellationToken);
            
            return new
            {
                Success = true,
                CurrentBranch = currentBranch,
                Branches = branches.Select(b => new
                {
                    Name = b,
                    IsCurrent = b == currentBranch
                }),
                TotalBranches = branches.Count(),
                IncludeRemote = includeRemote
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list branches");
            return new 
            { 
                Success = false, 
                Error = ex.Message,
                Branches = Array.Empty<object>()
            };
        }
    }

    [McpServerTool(Name = "git_create_branch", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Creates a new git branch and switches to it.")]
    public static async Task<object> CreateBranch(
        GitService gitService,
        ILogger<SourceControlToolsLogCategory> logger,
        [Description("Path to the git repository")] string repoPath,
        [Description("Name of the new branch")] string branchName,
        [Description("Optional starting point (branch or commit)")] string? startPoint = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Creating branch '{BranchName}' in: {RepoPath}", branchName, repoPath);
            
            var result = await gitService.CreateBranchAsync(repoPath, branchName, startPoint, cancellationToken);
            
            if (!result.Success)
            {
                return new
                {
                    Success = false,
                    Error = result.Error,
                    Output = result.Output
                };
            }
            
            return new
            {
                Success = true,
                BranchName = branchName,
                StartPoint = startPoint ?? "current HEAD",
                Message = $"Successfully created and switched to branch '{branchName}'",
                Output = result.Output
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create branch");
            return new 
            { 
                Success = false, 
                Error = ex.Message
            };
        }
    }

    [McpServerTool(Name = "git_checkout_branch", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Switches to an existing git branch.")]
    public static async Task<object> CheckoutBranch(
        GitService gitService,
        ILogger<SourceControlToolsLogCategory> logger,
        [Description("Path to the git repository")] string repoPath,
        [Description("Name of the branch to checkout")] string branchName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Checking out branch '{BranchName}' in: {RepoPath}", branchName, repoPath);
            
            var result = await gitService.CheckoutBranchAsync(repoPath, branchName, cancellationToken);
            
            if (!result.Success)
            {
                return new
                {
                    Success = false,
                    Error = result.Error,
                    Output = result.Output
                };
            }
            
            return new
            {
                Success = true,
                BranchName = branchName,
                Message = $"Successfully switched to branch '{branchName}'",
                Output = result.Output
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to checkout branch");
            return new 
            { 
                Success = false, 
                Error = ex.Message
            };
        }
    }

    [McpServerTool(Name = "git_stage_changes", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Stages file changes for commit. Can stage specific files or all changes.")]
    public static async Task<object> StageChanges(
        GitService gitService,
        ILogger<SourceControlToolsLogCategory> logger,
        [Description("Path to the git repository")] string repoPath,
        [Description("List of specific files to stage. If empty, stages all changes.")] IEnumerable<string>? files = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (files == null || !files.Any())
            {
                logger.LogInformation("Staging all changes in: {RepoPath}", repoPath);
                var result = await gitService.StageAllAsync(repoPath, cancellationToken);
                
                return new
                {
                    Success = result.Success,
                    StagedAll = true,
                    Output = result.Success ? "All changes staged successfully" : result.Error
                };
            }
            else
            {
                logger.LogInformation("Staging {Count} files in: {RepoPath}", files.Count(), repoPath);
                var result = await gitService.StageAsync(repoPath, files, cancellationToken);
                
                return new
                {
                    Success = result.Success,
                    StagedAll = false,
                    FilesStaged = files,
                    Output = result.Success ? $"{files.Count()} files staged" : result.Error
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to stage changes");
            return new 
            { 
                Success = false, 
                Error = ex.Message
            };
        }
    }

    [McpServerTool(Name = "git_commit", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Commits staged changes with a message.")]
    public static async Task<object> Commit(
        GitService gitService,
        ILogger<SourceControlToolsLogCategory> logger,
        [Description("Path to the git repository")] string repoPath,
        [Description("Commit message")] string message,
        [Description("Allow empty commit (no changes)")] bool allowEmpty = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Committing in: {RepoPath}", repoPath);
            
            var result = await gitService.CommitAsync(repoPath, message, allowEmpty, cancellationToken);
            
            if (!result.Success)
            {
                return new
                {
                    Success = false,
                    Error = result.Error,
                    Output = result.Output
                };
            }
            
            // Extract commit hash from output
            var commitHash = result.Output.Split(' ').SkipWhile(w => w != "]").FirstOrDefault()?.Trim(']', '[') ?? "unknown";
            
            return new
            {
                Success = true,
                CommitMessage = message,
                CommitHash = commitHash,
                Output = result.Output
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to commit");
            return new 
            { 
                Success = false, 
                Error = ex.Message
            };
        }
    }

    [McpServerTool(Name = "git_push", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Pushes committed changes to a remote repository.")]
    public static async Task<object> Push(
        GitService gitService,
        ILogger<SourceControlToolsLogCategory> logger,
        [Description("Path to the git repository")] string repoPath,
        [Description("Remote name (default: origin)")] string? remote = null,
        [Description("Branch name (default: current branch)")] string? branch = null,
        [Description("Force push (use with caution)")] bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentBranch = branch ?? await gitService.GetCurrentBranchAsync(repoPath, cancellationToken);
            logger.LogInformation("Pushing {Remote}/{Branch} from: {RepoPath}", remote ?? "origin", currentBranch, repoPath);
            
            var result = await gitService.PushAsync(repoPath, remote, branch, force, cancellationToken);
            
            if (!result.Success)
            {
                return new
                {
                    Success = false,
                    Error = result.Error,
                    Output = result.Output
                };
            }
            
            return new
            {
                Success = true,
                Remote = remote ?? "origin",
                Branch = currentBranch,
                ForcePush = force,
                Message = $"Successfully pushed to {remote ?? "origin"}/{currentBranch}",
                Output = result.Output
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to push");
            return new 
            { 
                Success = false, 
                Error = ex.Message
            };
        }
    }

    [McpServerTool(Name = "git_pull", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Pulls changes from a remote repository and merges them into the current branch.")]
    public static async Task<object> Pull(
        GitService gitService,
        ILogger<SourceControlToolsLogCategory> logger,
        [Description("Path to the git repository")] string repoPath,
        [Description("Remote name (default: origin)")] string? remote = null,
        [Description("Branch name (default: current branch)")] string? branch = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentBranch = branch ?? await gitService.GetCurrentBranchAsync(repoPath, cancellationToken);
            logger.LogInformation("Pulling {Remote}/{Branch} in: {RepoPath}", remote ?? "origin", currentBranch, repoPath);
            
            var result = await gitService.PullAsync(repoPath, remote, branch, cancellationToken);
            
            if (!result.Success)
            {
                return new
                {
                    Success = false,
                    Error = result.Error,
                    Output = result.Output
                };
            }
            
            return new
            {
                Success = true,
                Remote = remote ?? "origin",
                Branch = currentBranch,
                Message = $"Successfully pulled from {remote ?? "origin"}/{currentBranch}",
                Output = result.Output
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to pull");
            return new 
            { 
                Success = false, 
                Error = ex.Message
            };
        }
    }

    [McpServerTool(Name = "git_log", Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets the commit history/log for a repository or branch.")]
    public static async Task<object> GetLog(
        GitService gitService,
        ILogger<SourceControlToolsLogCategory> logger,
        [Description("Path to the git repository")] string repoPath,
        [Description("Number of commits to retrieve (default: 10)")] int count = 10,
        [Description("Specific branch to get log from (default: current branch)")] string? branch = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Getting commit log for: {RepoPath}", repoPath);
            
            var commits = await gitService.GetLogAsync(repoPath, count, branch, cancellationToken);
            var commitList = commits.ToList();
            
            return new
            {
                Success = true,
                Branch = branch ?? "current",
                Commits = commitList.Select(c => new
                {
                    c.Hash,
                    ShortHash = c.Hash.Substring(0, Math.Min(7, c.Hash.Length)),
                    c.AuthorName,
                    c.Date,
                    c.Message
                }),
                TotalCommits = commitList.Count
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get commit log");
            return new 
            { 
                Success = false, 
                Error = ex.Message,
                Commits = Array.Empty<object>()
            };
        }
    }

    [McpServerTool(Name = "git_diff", Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Shows differences between commits, branches, or working directory changes.")]
    public static async Task<object> GetDiff(
        GitService gitService,
        ILogger<SourceControlToolsLogCategory> logger,
        [Description("Path to the git repository")] string repoPath,
        [Description("Specific file to diff (optional)")] string? file = null,
        [Description("First commit reference (optional)")] string? commit1 = null,
        [Description("Second commit reference (optional)")] string? commit2 = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Getting diff for: {RepoPath}", repoPath);
            
            var result = await gitService.DiffAsync(repoPath, file, commit1, commit2, cancellationToken);
            
            if (!result.Success)
            {
                return new
                {
                    Success = false,
                    Error = result.Error
                };
            }
            
            return new
            {
                Success = true,
                File = file,
                CommitRange = commit1 != null && commit2 != null ? $"{commit1}..{commit2}" : commit1 ?? "working directory",
                DiffOutput = result.Output,
                HasChanges = !string.IsNullOrEmpty(result.Output)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get diff");
            return new 
            { 
                Success = false, 
                Error = ex.Message
            };
        }
    }
}
