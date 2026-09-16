// Copyright (c) 2025 Ahmed Mustafa

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using DotNetDevMCP.Testing;
using DotNetDevMCP.Testing.Mcp.Tools;

namespace DotNetDevMCP.Testing.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register Testing services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Testing services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="config">Optional testing service configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection WithTestingServices(this IServiceCollection services, TestingServiceConfig? config = null)
    {
        services.AddSingleton<TestingService>(sp =>
        {
            var orchestration = sp.GetRequiredService<Orchestration.OrchestrationService>();
            var testingConfig = config ?? TestingServiceConfig.Default;
            return new TestingService(orchestration, testingConfig);
        });

        return services;
    }

    /// <summary>
    /// Adds all Testing services and tools to the MCP service builder.
    /// </summary>
    /// <param name="builder">The MCP service builder.</param>
    /// <returns>The MCP service builder for chaining.</returns>
    public static IMcpServerBuilder WithTesting(this IMcpServerBuilder builder)
    {
        var toolAssembly = typeof(TestingTools).Assembly;

        return builder
            .WithToolsFromAssembly(toolAssembly)
            .WithPromptsFromAssembly(toolAssembly);
    }
}
