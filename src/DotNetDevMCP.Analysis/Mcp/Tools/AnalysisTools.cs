using ModelContextProtocol.Server;
using DotNetDevMCP.Analysis.Services;
using DotNetDevMCP.Analysis.Models;

namespace DotNetDevMCP.Analysis.Mcp.Tools;

/// <summary>
/// MCP tools for code analysis and metrics
/// </summary>
[McpServerToolType]
public sealed class AnalysisTools(
    CodeAnalysisService analysisService,
    ILogger<AnalysisTools> logger)
{
    private readonly CodeAnalysisService _analysisService = analysisService;
    private readonly ILogger<AnalysisTools> _logger = logger;

    /// <summary>
    /// Analyzes a .NET project or solution and returns comprehensive code metrics
    /// </summary>
    /// <param name="path">Path to the project file (.csproj), solution file (.sln), or directory</param>
    /// <param name="includeMetrics">Include detailed code metrics (default: true)</param>
    /// <param name="includeDependencies">Include dependency information (default: true)</param>
    [McpServerTool(Name = "dotnet_analyze_project")]
    public async Task<AnalysisResult> AnalyzeProjectAsync(
        string path,
        bool includeMetrics = true,
        bool includeDependencies = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Analyzing project at {Path}", path);
        
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            throw new FileNotFoundException($"Path not found: {path}");
        }

        return await _analysisService.AnalyzeAsync(path, cancellationToken);
    }

    /// <summary>
    /// Gets dependency information for a .NET project
    /// </summary>
    /// <param name="projectPath">Path to the project file (.csproj)</param>
    [McpServerTool(Name = "dotnet_get_dependencies")]
    public async Task<DependencyInfo> GetDependenciesAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Getting dependencies for {Path}", projectPath);
        
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"Project file not found: {projectPath}");
        }

        return await _analysisService.GetDependenciesAsync(projectPath, cancellationToken);
    }

    /// <summary>
    /// Analyzes code quality and identifies potential issues
    /// </summary>
    /// <param name="path">Path to the project or directory to analyze</param>
    [McpServerTool(Name = "dotnet_analyze_quality")]
    public async Task<CodeQualityMetrics> AnalyzeQualityAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Analyzing code quality at {Path}", path);
        
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            throw new FileNotFoundException($"Path not found: {path}");
        }

        return await _analysisService.AnalyzeQualityAsync(path, cancellationToken);
    }

    /// <summary>
    /// Scans for outdated NuGet packages in a project
    /// </summary>
    /// <param name="projectPath">Path to the project file (.csproj)</param>
    [McpServerTool(Name = "dotnet_scan_outdated_packages")]
    public async Task<IReadOnlyList<PackageDependency>> ScanOutdatedPackagesAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Scanning for outdated packages in {Path}", projectPath);
        
        var deps = await _analysisService.GetDependenciesAsync(projectPath, cancellationToken);
        
        // In a real implementation, this would check NuGet.org for latest versions
        // For now, return all direct dependencies as potentially outdated
        return deps.PackageDependencies
            .Where(d => d.IsDirect)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Detects circular dependencies in a solution
    /// </summary>
    /// <param name="solutionPath">Path to the solution file (.sln)</param>
    [McpServerTool(Name = "dotnet_detect_circular_dependencies")]
    public async Task<bool> DetectCircularDependenciesAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Detecting circular dependencies in {Path}", solutionPath);
        
        // Simplified implementation - would need full graph analysis in production
        var deps = await _analysisService.GetDependenciesAsync(solutionPath, cancellationToken);
        return deps.HasCircularDependencies;
    }
}
