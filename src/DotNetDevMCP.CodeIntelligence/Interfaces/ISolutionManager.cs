namespace DotNetDevMCP.CodeIntelligence.Interfaces;

public interface ISolutionManager : IDisposable {
    [MemberNotNullWhen(true, nameof(CurrentWorkspace), nameof(CurrentSolution))]
    bool IsSolutionLoaded { get; }
    MSBuildWorkspace? CurrentWorkspace { get; }
    Solution? CurrentSolution { get; }

    /// <summary>True once the background compilation warm-up started by LoadSolutionAsync has finished. Never awaited by other tools.</summary>
    bool IsWarm { get; }

    Task LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken);
    void UnloadSolution();

    Task<ISymbol?> FindRoslynSymbolAsync(string fullyQualifiedName, CancellationToken cancellationToken);
    Task<INamedTypeSymbol?> FindRoslynNamedTypeSymbolAsync(string fullyQualifiedTypeName, CancellationToken cancellationToken);
    Task<Type?> FindReflectionTypeAsync(string fullyQualifiedTypeName, CancellationToken cancellationToken);
    Task<IEnumerable<Type>> SearchReflectionTypesAsync(string regexPattern, CancellationToken cancellationToken);

    IEnumerable<Project> GetProjects();
    Project? GetProjectByName(string projectName); Task<SemanticModel?> GetSemanticModelAsync(DocumentId documentId, CancellationToken cancellationToken);
    Task<Compilation?> GetCompilationAsync(ProjectId projectId, CancellationToken cancellationToken);
    Task ReloadSolutionFromDiskAsync(CancellationToken cancellationToken);
    void RefreshCurrentSolution();
}