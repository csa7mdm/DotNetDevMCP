// Copyright (c) 2025 Ahmed Mustafa

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetDevMCP.SourceControl.Services;

/// <summary>
/// Result of a git operation
/// </summary>
public record GitResult(
    bool Success,
    string Output,
    string Error,
    TimeSpan Duration);

/// <summary>
/// Information about a git repository
/// </summary>
public record GitRepoInfo(
    string RootPath,
    string CurrentBranch,
    bool IsDirty,
    int AheadCount,
    int BehindCount,
    IEnumerable<string> Remotes,
    IEnumerable<string> UntrackedFiles,
    IEnumerable<GitChange> Changes);

/// <summary>
/// Represents a file change in git
/// </summary>
public record GitChange(
    string FilePath,
    FileStatus Status,
    string? OldFilePath = null);

/// <summary>
/// Status of a file in git
/// </summary>
public enum FileStatus
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Untracked,
    Conflicted
}

/// <summary>
/// Service for git source control operations
/// </summary>
public class GitService
{
    private static readonly Regex BranchRegex = new(@"^\*\s+(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex StatusRegex = new(@"^([AMDRCU\?\!])\s*(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Gets information about the git repository
    /// </summary>
    public async Task<GitRepoInfo> GetRepoInfoAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var rootPath = await RunGitCommandAsync(repoPath, "rev-parse --show-toplevel", cancellationToken);
            if (!rootPath.Success)
                throw new InvalidOperationException("Not a git repository");

            var branchResult = await RunGitCommandAsync(repoPath.RootPath, "branch --show-current", cancellationToken);
            var currentBranch = branchResult.Output.Trim();

            var statusResult = await RunGitCommandAsync(repoPath.RootPath, "status --porcelain", cancellationToken);
            var changes = ParseStatus(statusResult.Output);

            var remoteResult = await RunGitCommandAsync(repoPath.RootPath, "remote", cancellationToken);
            var remotes = remoteResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var (ahead, behind) = await GetAheadBehindCountAsync(repoPath.RootPath, currentBranch, cancellationToken);

            return new GitRepoInfo(
                RootPath: rootPath.Output.Trim(),
                CurrentBranch: currentBranch,
                IsDirty: changes.Any() || statusResult.Output.Contains("Untracked files"),
                AheadCount: ahead,
                BehindCount: behind,
                Remotes: remotes,
                UntrackedFiles: GetUntrackedFiles(statusResult.Output),
                Changes: changes.Where(c => c.Status != FileStatus.Untracked));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get repository info: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets the current branch name
    /// </summary>
    public async Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var result = await RunGitCommandAsync(repoPath, "branch --show-current", cancellationToken);
        EnsureSuccess(result);
        return result.Output.Trim();
    }

    /// <summary>
    /// Lists all branches
    /// </summary>
    public async Task<IEnumerable<string>> GetBranchesAsync(string repoPath, bool includeRemote = false, CancellationToken cancellationToken = default)
    {
        var command = includeRemote ? "branch -a" : "branch";
        var result = await RunGitCommandAsync(repoPath, command, cancellationToken);
        EnsureSuccess(result);

        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimStart('*', ' ').Replace("remotes/", ""))
            .Distinct();
    }

    /// <summary>
    /// Creates a new branch
    /// </summary>
    public async Task<GitResult> CreateBranchAsync(string repoPath, string branchName, string? startPoint = null, CancellationToken cancellationToken = default)
    {
        var command = startPoint != null 
            ? $"checkout -b {branchName} {startPoint}" 
            : $"checkout -b {branchName}";
        
        return await RunGitCommandAsync(repoPath, command, cancellationToken);
    }

    /// <summary>
    /// Switches to a branch
    /// </summary>
    public async Task<GitResult> CheckoutBranchAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
    {
        return await RunGitCommandAsync(repoPath, $"checkout {branchName}", cancellationToken);
    }

    /// <summary>
    /// Stages file changes
    /// </summary>
    public async Task<GitResult> StageAsync(string repoPath, IEnumerable<string> files, CancellationToken cancellationToken = default)
    {
        var fileList = string.Join(" ", files.Select(f => $"\"{f}\""));
        return await RunGitCommandAsync(repoPath, $"add {fileList}", cancellationToken);
    }

    /// <summary>
    /// Stages all changes
    /// </summary>
    public async Task<GitResult> StageAllAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return await RunGitCommandAsync(repoPath, "add -A", cancellationToken);
    }

    /// <summary>
    /// Commits staged changes
    /// </summary>
    public async Task<GitResult> CommitAsync(string repoPath, string message, bool allowEmpty = false, CancellationToken cancellationToken = default)
    {
        var command = allowEmpty 
            ? $"commit --allow-empty -m \"{message}\"" 
            : $"commit -m \"{message}\"";
        
        return await RunGitCommandAsync(repoPath, command, cancellationToken);
    }

    /// <summary>
    /// Pushes changes to remote
    /// </summary>
    public async Task<GitResult> PushAsync(string repoPath, string? remote = null, string? branch = null, bool force = false, CancellationToken cancellationToken = default)
    {
        var command = "push";
        if (force) command += " --force";
        if (remote != null) command += $" {remote}";
        if (branch != null) command += $" {branch}";

        return await RunGitCommandAsync(repoPath, command, cancellationToken);
    }

    /// <summary>
    /// Pulls changes from remote
    /// </summary>
    public async Task<GitResult> PullAsync(string repoPath, string? remote = null, string? branch = null, CancellationToken cancellationToken = default)
    {
        var command = "pull";
        if (remote != null) command += $" {remote}";
        if (branch != null) command += $" {branch}";

        return await RunGitCommandAsync(repoPath, command, cancellationToken);
    }

    /// <summary>
    /// Fetches changes from remote
    /// </summary>
    public async Task<GitResult> FetchAsync(string repoPath, string? remote = null, CancellationToken cancellationToken = default)
    {
        var command = remote != null ? $"fetch {remote}" : "fetch --all";
        return await RunGitCommandAsync(repoPath, command, cancellationToken);
    }

    /// <summary>
    /// Gets commit log
    /// </summary>
    public async Task<IEnumerable<GitCommit>> GetLogAsync(string repoPath, int count = 10, string? branch = null, CancellationToken cancellationToken = default)
    {
        var format = "--pretty=format:%H|%an|%ae|%ad|%s";
        var command = $"log {format} -{count}";
        if (branch != null) command += $" {branch}";

        var result = await RunGitCommandAsync(repoPath, command, cancellationToken);
        EnsureSuccess(result);

        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split('|', 5);
                return new GitCommit(
                    Hash: parts[0],
                    AuthorName: parts[1],
                    AuthorEmail: parts[2],
                    Date: parts[3],
                    Message: parts[4]);
            });
    }

    /// <summary>
    /// Shows diff for a file or between commits
    /// </summary>
    public async Task<GitResult> DiffAsync(string repoPath, string? file = null, string? commit1 = null, string? commit2 = null, CancellationToken cancellationToken = default)
    {
        var command = "diff";
        if (commit1 != null && commit2 != null)
            command = $"diff {commit1} {commit2}";
        else if (commit1 != null)
            command = $"diff {commit1}";
        
        if (file != null)
            command += $" -- \"{file}\"";

        return await RunGitCommandAsync(repoPath, command, cancellationToken);
    }

    /// <summary>
    /// Runs a git command
    /// </summary>
    private async Task<GitResult> RunGitCommandAsync(string repoPath, string arguments, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = repoPath
            };

            using var process = new Process { StartInfo = startInfo };
            var output = new List<string>();
            var errors = new List<string>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                    output.Add(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                    errors.Add(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            stopwatch.Stop();

            return new GitResult(
                Success: process.ExitCode == 0,
                Output: string.Join(Environment.NewLine, output),
                Error: string.Join(Environment.NewLine, errors),
                Duration: stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new GitResult(
                Success: false,
                Output: string.Empty,
                Error: ex.Message,
                Duration: stopwatch.Elapsed);
        }
    }

    private static void EnsureSuccess(GitResult result)
    {
        if (!result.Success)
            throw new InvalidOperationException($"Git command failed: {result.Error}");
    }

    private static IEnumerable<GitChange> ParseStatus(string output)
    {
        var changes = new List<GitChange>();
        var matches = StatusRegex.Matches(output);

        foreach (Match match in matches)
        {
            var statusCode = match.Groups[1].Value;
            var filePath = match.Groups[2].Value.Trim();

            var status = statusCode switch
            {
                "M" => FileStatus.Modified,
                "A" => FileStatus.Added,
                "D" => FileStatus.Deleted,
                "R" => FileStatus.Renamed,
                "C" => FileStatus.Conflicted,
                "U" => FileStatus.Conflicted,
                "?" => FileStatus.Untracked,
                _ => FileStatus.Modified
            };

            changes.Add(new GitChange(filePath, status));
        }

        return changes;
    }

    private static IEnumerable<string> GetUntrackedFiles(string output)
    {
        return StatusRegex.Matches(output)
            .Where(m => m.Groups[1].Value == "?")
            .Select(m => m.Groups[2].Value.Trim());
    }

    private async Task<(int Ahead, int Behind)> GetAheadBehindCountAsync(string repoPath, string branch, CancellationToken cancellationToken = default)
    {
        var result = await RunGitCommandAsync(repoPath, $"rev-list --left-right --count origin/{branch}...{branch}", cancellationToken);
        if (!result.Success)
            return (0, 0);

        var parts = result.Output.Trim().Split('\t');
        if (parts.Length != 2)
            return (0, 0);

        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }
}

/// <summary>
/// Represents a git commit
/// </summary>
public record GitCommit(
    string Hash,
    string AuthorName,
    string AuthorEmail,
    string Date,
    string Message);
