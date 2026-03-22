using ModelContextProtocol.Server;
using DotNetDevMCP.Monitoring.Services;
using DotNetDevMCP.Monitoring.Models;

namespace DotNetDevMCP.Monitoring.Mcp.Tools;

/// <summary>
/// MCP tools for performance monitoring and profiling
/// </summary>
[McpServerToolType]
public sealed class MonitoringTools(
    PerformanceMonitoringService monitoringService,
    ILogger<MonitoringTools> logger)
{
    private readonly PerformanceMonitoringService _monitoringService = monitoringService;
    private readonly ILogger<MonitoringTools> _logger = logger;

    /// <summary>
    /// Gets current performance metrics for the application
    /// </summary>
    [McpServerTool(Name = "dotnet_get_performance_metrics")]
    public async Task<PerformanceMetrics> GetPerformanceMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Getting performance metrics");
        return await _monitoringService.GetPerformanceMetricsAsync(cancellationToken);
    }

    /// <summary>
    /// Gets system resource utilization metrics
    /// </summary>
    [McpServerTool(Name = "dotnet_get_resource_utilization")]
    public async Task<ResourceUtilization> GetResourceUtilizationAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Getting resource utilization");
        return await _monitoringService.GetResourceUtilizationAsync(cancellationToken);
    }

    /// <summary>
    /// Checks application health status
    /// </summary>
    [McpServerTool(Name = "dotnet_check_health")]
    public async Task<HealthStatus> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Checking application health");
        return await _monitoringService.CheckHealthAsync(cancellationToken);
    }

    /// <summary>
    /// Starts a profiling session to collect performance data
    /// </summary>
    /// <param name="sessionName">Name for the profiling session</param>
    /// <param name="durationSeconds">Duration of the profiling session in seconds</param>
    /// <param name="collectCpuSamples">Collect CPU samples (default: true)</param>
    /// <param name="collectMemorySamples">Collect memory samples (default: true)</param>
    /// <param name="collectGcEvents">Collect GC events (default: true)</param>
    [McpServerTool(Name = "dotnet_start_profiling_session")]
    public async Task<ProfilingResults> StartProfilingSessionAsync(
        string sessionName,
        int durationSeconds = 10,
        bool collectCpuSamples = true,
        bool collectMemorySamples = true,
        bool collectGcEvents = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Starting profiling session '{Session}' for {Duration}s", 
            sessionName, durationSeconds);
        
        var config = new ProfilingConfig(
            SessionName: sessionName,
            Duration: TimeSpan.FromSeconds(durationSeconds),
            CollectCpuSamples: collectCpuSamples,
            CollectMemorySamples: collectMemorySamples,
            CollectGcEvents: collectGcEvents,
            CollectThreadEvents: true
        );
        
        return await _monitoringService.StartProfilingSessionAsync(config, cancellationToken);
    }

    /// <summary>
    /// Gets garbage collection statistics
    /// </summary>
    [McpServerTool(Name = "dotnet_get_gc_stats")]
    public async Task<object> GetGcStatsAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Getting GC statistics");
        
        return new
        {
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            TotalMemoryBytes = GC.GetTotalMemory(false),
            GcHeapSizeBytes = GC.GetGCMemoryInfo().HeapSizeBytes,
            FragmentedBytes = GC.GetGCMemoryInfo().FragmentedBytes,
            MemoryLoadBytes = GC.GetGCMemoryInfo().MemoryLoadBytes,
            TotalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            CapturedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Forces a garbage collection
    /// </summary>
    /// <param name="generation">Generation to collect (0-2, default: 2 for full GC)</param>
    /// <param name="blocking">Whether to block until collection completes (default: true)</param>
    [McpServerTool(Name = "dotnet_force_gc")]
    public Task<object> ForceGcAsync(
        int generation = 2,
        bool blocking = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP: Forcing GC collection gen {Generation}", generation);
        
        if (blocking)
        {
            GC.Collect(generation, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        else
        {
            GC.Collect(generation, GCCollectionMode.Forced, blocking: false, compacting: true);
        }
        
        return Task.FromResult(new
        {
            Generation = generation,
            Blocking = blocking,
            TotalMemoryBytes = GC.GetTotalMemory(false),
            Timestamp = DateTime.UtcNow
        });
    }
}
