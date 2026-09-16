// Copyright (c) 2025 Ahmed Mustafa

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using DotNetDevMCP.Build;
using DotNetDevMCP.Build.Mcp.Tools;

namespace DotNetDevMCP.Build.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register Build services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Build services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection WithBuildServices(this IServiceCollection services)
    {
        services.AddSingleton<BuildService>();
        return services;
    }

    /// <summary>
    /// Adds all Build services and tools to the MCP service builder.
    /// </summary>
    /// <param name="builder">The MCP service builder.</param>
    /// <returns>The MCP service builder for chaining.</returns>
    public static IMcpServerBuilder WithBuild(this IMcpServerBuilder builder)
    {
        var toolAssembly = typeof(BuildTools).Assembly;

        return builder
            .WithToolsFromAssembly(toolAssembly)
            .WithPromptsFromAssembly(toolAssembly);
    }
}
