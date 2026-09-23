using System.Reflection;
using DotNetDevMCP.CodeIntelligence.Extensions;
using DotNetDevMCP.CodeIntelligence.Interfaces;
using DotNetDevMCP.CodeIntelligence.Services;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetDevMCP.CodeIntelligence.Tests;

/// <summary>
/// Covers the fix for edit tools silently creating a git branch and commit: git integration must be
/// opt-in (via --git-commit-edits), off by default.
/// </summary>
public class GitIntegrationOptInTests {
    [Fact]
    public void WithCodeIntelligenceServices_default_registers_the_no_op_git_service() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.WithCodeIntelligenceServices();

        using var provider = services.BuildServiceProvider();
        var gitService = provider.GetRequiredService<IGitService>();

        Assert.IsType<NoOpGitService>(gitService);
        Assert.False(gitService.IsEnabled);
    }

    [Fact]
    public void WithCodeIntelligenceServices_enableGit_true_registers_the_real_git_service() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.WithCodeIntelligenceServices(enableGit: true);

        using var provider = services.BuildServiceProvider();
        var gitService = provider.GetRequiredService<IGitService>();

        Assert.IsType<GitService>(gitService);
        Assert.True(gitService.IsEnabled);
    }

    [Fact]
    public async Task Default_git_service_does_not_change_branch_or_create_a_commit_in_a_real_repo() {
        var repoDir = Directory.CreateTempSubdirectory("dotnetdevmcp-git-optin-test-");
        try {
            Repository.Init(repoDir.FullName);

            var solutionPath = Path.Combine(repoDir.FullName, "Fake.sln");
            File.WriteAllText(solutionPath, "fake solution");

            string originalBranch;
            string originalTip;
            using (var repo = new Repository(repoDir.FullName)) {
                Commands.Stage(repo, "*");
                var signature = new Signature("Test", "test@example.com", DateTimeOffset.Now);
                repo.Commit("Initial commit", signature, signature);

                originalBranch = repo.Head.FriendlyName;
                originalTip = repo.Head.Tip.Sha;
            }

            // Simulate what an edit tool changes on disk before it (optionally) hands off to git.
            var changedFile = Path.Combine(repoDir.FullName, "Changed.cs");
            File.WriteAllText(changedFile, "// edited by the tool");

            var services = new ServiceCollection();
            services.AddLogging();
            services.WithCodeIntelligenceServices(); // default options - git integration off

            using var provider = services.BuildServiceProvider();
            var gitService = provider.GetRequiredService<IGitService>();
            Assert.IsType<NoOpGitService>(gitService);

            // Exactly the sequence CodeModificationService/DocumentOperationsService run after
            // applying an edit: check for a repo, ensure a sharptools branch, then commit.
            var isRepository = await gitService.IsRepositoryAsync(solutionPath);
            Assert.False(isRepository, "The no-op git service must never report a real repo as one it manages.");

            await gitService.EnsureSharpToolsBranchAsync(solutionPath);
            await gitService.CommitChangesAsync(solutionPath, new[] { changedFile }, "Should not be committed");

            using (var repo = new Repository(repoDir.FullName)) {
                Assert.Equal(originalBranch, repo.Head.FriendlyName);
                Assert.Equal(originalTip, repo.Head.Tip.Sha);
                Assert.DoesNotContain(repo.Branches, b => b.FriendlyName.StartsWith("sharptools/", StringComparison.OrdinalIgnoreCase));

                var status = repo.RetrieveStatus();
                Assert.Contains(status, e => e.FilePath == "Changed.cs" && e.State.HasFlag(FileStatus.NewInWorkdir));
            }
        } finally {
            try { Directory.Delete(repoDir.FullName, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    [Fact]
    public async Task UndoLastChangeAsync_returns_a_clear_message_instead_of_failing_obscurely_when_git_is_disabled() {
        var solutionManager = new FakeSolutionManagerForUndo();
        var service = new CodeModificationService(solutionManager, new NoOpGitService(), NullLogger<CodeModificationService>.Instance);

        var (success, message) = await service.UndoLastChangeAsync(CancellationToken.None);

        Assert.False(success);
        Assert.Contains("--git-commit-edits", message);
    }

    /// <summary>
    /// Minimal ISolutionManager stand-in just to reach the git-disabled check in UndoLastChangeAsync.
    /// The interface exposes a concrete MSBuildWorkspace, and Solution.FilePath is only settable via
    /// the workspace's own (protected) OnSolutionAdded, so a bare SolutionInfo is attached via
    /// reflection rather than actually opening a project on disk - this only needs a Solution whose
    /// FilePath is non-null, not a real, buildable one.
    /// </summary>
    private sealed class FakeSolutionManagerForUndo : ISolutionManager {
        public bool IsSolutionLoaded => true;
        public Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace? CurrentWorkspace { get; } = CreateWorkspaceWithFakeSolution();
        public Solution? CurrentSolution => CurrentWorkspace!.CurrentSolution;

        private static Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace CreateWorkspaceWithFakeSolution() {
            var workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create();
            var solutionInfo = SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), filePath: "C:/fake/Fake.sln");
            var onSolutionAdded = typeof(Workspace).GetMethod("OnSolutionAdded", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Workspace.OnSolutionAdded not found - Roslyn API shape changed.");
            onSolutionAdded.Invoke(workspace, new object[] { solutionInfo });
            return workspace;
        }

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
        public void Dispose() => CurrentWorkspace?.Dispose();
    }
}
