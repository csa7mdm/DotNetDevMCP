// Copyright (c) 2025 Ahmed Mustafa

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using DotNetDevMCP.SourceControl.Services;
using DotNetDevMCP.SourceControl.Mcp.Tools;

namespace DotNetDevMCP.SourceControl.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register SourceControl services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all SourceControl services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection WithSourceControlServices(this IServiceCollection services)
    {
        services.AddSingleton<GitService>();
        return services;
    }

    /// <summary>
    /// Adds all SourceControl services and tools to the MCP service builder.
    /// </summary>
    /// <param name="builder">The MCP service builder.</param>
    /// <returns>The MCP service builder for chaining.</returns>
    public static IMcpServerBuilder WithSourceControl(this IMcpServerBuilder builder)
    {
        var toolAssembly = typeof(SourceControlTools).Assembly;

        return builder
            .WithToolsFromAssembly(toolAssembly)
            .WithPromptsFromAssembly(toolAssembly);
    }
}
