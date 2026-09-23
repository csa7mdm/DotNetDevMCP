using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DotNetDevMCP.CodeIntelligence.Interfaces;
using DotNetDevMCP.CodeIntelligence.Services;
using DotNetDevMCP.CodeIntelligence.Mcp.Tools;
using System.Reflection;

namespace DotNetDevMCP.CodeIntelligence.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register CodeIntelligence services.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds all CodeIntelligence services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <param name="enableGit">
    /// When true, edit tools (RenameSymbol, OverwriteMember, AddMember, MoveMember, FindAndReplace,
    /// CreateRoslynDocument, OverwriteRoslynDocument, ManageUsings, ManageAttributes) create a
    /// "sharptools/&lt;timestamp&gt;" branch and commit after every change, and SharpTool_Undo reverts by
    /// resetting that commit. Defaults to false (opt-in via --git-commit-edits) because switching a
    /// user's branch and creating commits behind their back is surprising. When false, edits still
    /// apply to disk and return the same compile-check output - they just never touch git.
    /// </param>
    public static IServiceCollection WithCodeIntelligenceServices(this IServiceCollection services, bool enableGit = false, string? buildConfiguration = null) {
        services.AddSingleton<IFuzzyFqnLookupService, FuzzyFqnLookupService>();
        services.AddSingleton<ISolutionManager>(sp => 
            new SolutionManager(
                sp.GetRequiredService<ILogger<SolutionManager>>(), 
                sp.GetRequiredService<IFuzzyFqnLookupService>(),
                buildConfiguration
            )
        );
        services.AddSingleton<ICodeAnalysisService, CodeAnalysisService>();
        if (enableGit) {
            services.AddSingleton<IGitService, GitService>();
        } else {
            services.AddSingleton<IGitService, NoOpGitService>();
        }
        services.AddSingleton<ICodeModificationService, CodeModificationService>();
        services.AddSingleton<IEditorConfigProvider, EditorConfigProvider>();
        services.AddSingleton<IDocumentOperationsService, DocumentOperationsService>();
        services.AddSingleton<IComplexityAnalysisService, ComplexityAnalysisService>();
        services.AddSingleton<ISemanticSimilarityService, SemanticSimilarityService>();
        services.AddSingleton<ISourceResolutionService, SourceResolutionService>();

        return services;
    }

    /// <summary>
    /// Adds all CodeIntelligence services and tools to the MCP service builder.
    /// </summary>
    /// <param name="builder">The MCP service builder.</param>
    /// <returns>The MCP service builder for chaining.</returns>
    public static IMcpServerBuilder WithCodeIntelligence(this IMcpServerBuilder builder) {
        var toolAssembly = typeof(AnalysisTools).Assembly;

        return builder
            .WithToolsFromAssembly(toolAssembly)
            .WithPromptsFromAssembly(toolAssembly);
    }
}