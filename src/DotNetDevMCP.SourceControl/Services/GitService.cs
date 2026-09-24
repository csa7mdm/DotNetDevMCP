// Copyright (c) 2025 Ahmed Mustafa

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetDevMCP.Core;

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
            var rootResult = await RunGitCommandAsync(repoPath, ["rev-parse", "--show-toplevel"], cancellationToken);
            if (!rootResult.Success)
                throw new InvalidOperationException("Not a git repository");
            var rootPath = rootResult.Output.Trim();

            var branchResult = await RunGitCommandAsync(rootPath, ["branch", "--show-current"], cancellationToken);
            var currentBranch = branchResult.Output.Trim();

            var statusResult = await RunGitCommandAsync(rootPath, ["status", "--porcelain"], cancellationToken);
            var changes = ParseStatus(statusResult.Output);

            var remoteResult = await RunGitCommandAsync(rootPath, ["remote"], cancellationToken);
            var remotes = remoteResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var (ahead, behind) = await GetAheadBehindCountAsync(rootPath, currentBranch, cancellationToken);

            return new GitRepoInfo(
                RootPath: rootPath,
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
        var result = await RunGitCommandAsync(repoPath, ["branch", "--show-current"], cancellationToken);
        EnsureSuccess(result);
        return result.Output.Trim();
    }

    /// <summary>
    /// Lists all branches
    /// </summary>
    public async Task<IEnumerable<string>> GetBranchesAsync(string repoPath, bool includeRemote = false, CancellationToken cancellationToken = default)
    {
        var args = includeRemote ? new[] { "branch", "-a" } : new[] { "branch" };
        var result = await RunGitCommandAsync(repoPath, args, cancellationToken);
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
        var refError = GitRefValidation.Validate(branchName, nameof(branchName)) ?? GitRefValidation.Validate(startPoint, nameof(startPoint));
        if (refError != null) return Failed(refError);

        var args = new List<string> { "checkout", "-b", branchName };
        if (startPoint != null) args.Add(startPoint);

        return await RunGitCommandAsync(repoPath, args, cancellationToken);
    }

    /// <summary>
    /// Switches to a branch
    /// </summary>
    public async Task<GitResult> CheckoutBranchAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
    {
        var refError = GitRefValidation.Validate(branchName, nameof(branchName));
        if (refError != null) return Failed(refError);

        return await RunGitCommandAsync(repoPath, ["checkout", branchName], cancellationToken);
    }

    /// <summary>
    /// Stages file changes
    /// </summary>
    public async Task<GitResult> StageAsync(string repoPath, IEnumerable<string> files, CancellationToken cancellationToken = default)
    {
        // "--" ends option parsing: a file named e.g. "-x" is then unambiguously a pathspec, not a flag.
        var args = new List<string> { "add", "--" };
        args.AddRange(files);
        return await RunGitCommandAsync(repoPath, args, cancellationToken);
    }

    /// <summary>
    /// Stages all changes
    /// </summary>
    public async Task<GitResult> StageAllAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return await RunGitCommandAsync(repoPath, ["add", "-A"], cancellationToken);
    }

    /// <summary>
    /// Commits staged changes
    /// </summary>
    public async Task<GitResult> CommitAsync(string repoPath, string message, bool allowEmpty = false, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "commit" };
        if (allowEmpty) args.Add("--allow-empty");
        args.Add("-m");
        args.Add(message);

        return await RunGitCommandAsync(repoPath, args, cancellationToken);
    }

    /// <summary>
    /// Pushes changes to remote
    /// </summary>
    public async Task<GitResult> PushAsync(string repoPath, string? remote = null, string? branch = null, bool force = false, CancellationToken cancellationToken = default)
    {
        var refError = GitRefValidation.Validate(remote, nameof(remote)) ?? GitRefValidation.Validate(branch, nameof(branch));
        if (refError != null) return Failed(refError);

        var args = new List<string> { "push" };
        if (force) args.Add("--force");
        if (remote != null) args.Add(remote);
        if (branch != null) args.Add(branch);

        return await RunGitCommandAsync(repoPath, args, cancellationToken);
    }

    /// <summary>
    /// Pulls changes from remote
    /// </summary>
    public async Task<GitResult> PullAsync(string repoPath, string? remote = null, string? branch = null, CancellationToken cancellationToken = default)
    {
        var refError = GitRefValidation.Validate(remote, nameof(remote)) ?? GitRefValidation.Validate(branch, nameof(branch));
        if (refError != null) return Failed(refError);

        var args = new List<string> { "pull" };
        if (remote != null) args.Add(remote);
        if (branch != null) args.Add(branch);

        return await RunGitCommandAsync(repoPath, args, cancellationToken);
    }

    /// <summary>
    /// Fetches changes from remote
    /// </summary>
    public async Task<GitResult> FetchAsync(string repoPath, string? remote = null, CancellationToken cancellationToken = default)
    {
        var refError = GitRefValidation.Validate(remote, nameof(remote));
        if (refError != null) return Failed(refError);

        var args = remote != null ? new[] { "fetch", remote } : new[] { "fetch", "--all" };
        return await RunGitCommandAsync(repoPath, args, cancellationToken);
    }

    /// <summary>
    /// Gets commit log
    /// </summary>
    public async Task<IEnumerable<GitCommit>> GetLogAsync(string repoPath, int count = 10, string? branch = null, CancellationToken cancellationToken = default)
    {
        var refError = GitRefValidation.Validate(branch, nameof(branch));
        if (refError != null) throw new InvalidOperationException(refError);

        var args = new List<string> { "log", "--pretty=format:%H|%an|%ae|%ad|%s", $"-{count}" };
        if (branch != null) args.Add(branch);

        var result = await RunGitCommandAsync(repoPath, args, cancellationToken);
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
        var refError = GitRefValidation.Validate(commit1, nameof(commit1)) ?? GitRefValidation.Validate(commit2, nameof(commit2));
        if (refError != null) return Failed(refError);

        var args = new List<string> { "diff" };
        if (commit1 != null) args.Add(commit1);
        if (commit2 != null) args.Add(commit2);
        // "--" ends option/ref parsing: whatever follows is unambiguously the pathspec, never re-parsed as a ref or flag.
        if (file != null) { args.Add("--"); args.Add(file); }

        return await RunGitCommandAsync(repoPath, args, cancellationToken);
    }

    private static GitResult Failed(string error) => new(Success: false, Output: string.Empty, Error: error, Duration: TimeSpan.Zero);

    /// <summary>
    /// Runs a git command. Arguments go through <see cref="ProcessStartInfo.ArgumentList"/> so each value is
    /// exactly one argument (.NET does the quoting) and can never be split into extra arguments the way
    /// concatenating into a single argument string would allow, e.g. a diff base of "--output=C:/x.txt".
    /// </summary>
    private async Task<GitResult> RunGitCommandAsync(string repoPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(repoPath))
            return Failed("repoPath is required.");

        string fullRepoPath;
        try
        {
            fullRepoPath = Path.GetFullPath(repoPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failed($"Invalid repoPath '{repoPath}': {ex.Message}");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = fullRepoPath
            };
            foreach (var arg in arguments) startInfo.ArgumentList.Add(arg);
            ChildProcess.Prepare(startInfo);

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
        // branch comes from our own "branch --show-current" output, not a caller-supplied value, so it
        // doesn't need GitRefValidation; it's still one ArgumentList entry, so no quoting is needed either.
        var result = await RunGitCommandAsync(repoPath, ["rev-list", "--left-right", "--count", $"origin/{branch}...{branch}"], cancellationToken);
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
