using Microsoft.Extensions.DependencyInjection;
using DotNetDevMCP.Monitoring.Services;

namespace DotNetDevMCP.Monitoring.Extensions;

/// <summary>
/// Extension methods for registering Monitoring services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Monitoring services to the service collection
    /// </summary>
    public static IServiceCollection AddMonitoringServices(this IServiceCollection services)
    {
        services.AddSingleton<PerformanceMonitoringService>();
        return services;
    }
}
