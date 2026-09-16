// Copyright (c) 2025 Ahmed Mustafa

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using DotNetDevMCP.Orchestration;
using DotNetDevMCP.Orchestration.Mcp.Tools;

namespace DotNetDevMCP.Orchestration.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register Orchestration services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Orchestration services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection WithOrchestrationServices(this IServiceCollection services)
    {
        services.AddSingleton<IOrchestrationService, OrchestrationService>();
        return services;
    }

    /// <summary>
    /// Adds all Orchestration services and tools to the MCP service builder.
    /// </summary>
    /// <param name="builder">The MCP service builder.</param>
    /// <returns>The MCP service builder for chaining.</returns>
    public static IMcpServerBuilder WithOrchestration(this IMcpServerBuilder builder)
    {
        var toolAssembly = typeof(OrchestrationTools).Assembly;

        return builder
            .WithToolsFromAssembly(toolAssembly)
            .WithPromptsFromAssembly(toolAssembly);
    }
}
