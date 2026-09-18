// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Testing.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace DotNetDevMCP.Testing.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection WithTestingServices(this IServiceCollection services)
    {
        services.AddSingleton<TestRunner>();
        services.AddSingleton<AffectedTestFinder>(); // needs ISolutionManager from CodeIntelligence
        return services;
    }

    public static IMcpServerBuilder WithTesting(this IMcpServerBuilder builder) => builder.WithTools([typeof(TestingTools)]);
}
