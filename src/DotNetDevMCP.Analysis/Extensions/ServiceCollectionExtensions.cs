using Microsoft.Extensions.DependencyInjection;
using DotNetDevMCP.Analysis.Services;

namespace DotNetDevMCP.Analysis.Extensions;

/// <summary>
/// Extension methods for registering Analysis services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Analysis services to the service collection
    /// </summary>
    public static IServiceCollection AddAnalysisServices(this IServiceCollection services)
    {
        services.AddSingleton<CodeAnalysisService>();
        return services;
    }
}
