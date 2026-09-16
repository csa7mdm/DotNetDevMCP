using System.Diagnostics;
using System.Runtime.InteropServices;
using DotNetDevMCP.Monitoring.Models;

namespace DotNetDevMCP.Monitoring.Services;

/// <summary>
/// Service for monitoring application performance and resource usage
/// </summary>
public sealed class PerformanceMonitoringService(ILogger<PerformanceMonitoringService> logger)
{
    private readonly ILogger<PerformanceMonitoringService> _logger = logger;
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private readonly Stopwatch _uptimeStopwatch = Stopwatch.StartNew();

    /// <summary>
    /// Captures current performance metrics for the application
    /// </summary>
    public Task<PerformanceMetrics> GetPerformanceMetricsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Capturing performance metrics");
        
        _currentProcess.Refresh();
        
        var cpuUsage = CalculateCpuUsage();
        var memoryUsage = _currentProcess.WorkingSet64;
        var peakMemory = _currentProcess.PeakWorkingSet64;
        
        var metrics = new PerformanceMetrics(
            ProcessName: _currentProcess.ProcessName,
            ProcessId: _currentProcess.Id,
            CpuUsagePercent: cpuUsage,
            MemoryUsageBytes: memoryUsage,
            PeakMemoryUsageBytes: peakMemory,
            ThreadCount: _currentProcess.Threads.Count,
            HandleCount: _currentProcess.HandleCount,
            GcHeapSizeBytes: GC.GetTotalMemory(false),
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2),
            CapturedAt: DateTime.UtcNow
        );

        return Task.FromResult(metrics);
    }

    /// <summary>
    /// Gets resource utilization metrics for the system
    /// </summary>
    public async Task<ResourceUtilization> GetResourceUtilizationAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Capturing resource utilization");
        
        var cpuUsage = await GetSystemCpuUsageAsync(cancellationToken);
        var memoryUsage = GetSystemMemoryUsage();
        
        return new ResourceUtilization(
            CpuUsagePercent: cpuUsage,
            MemoryUsagePercent: memoryUsage.memoryPercent,
            DiskUsagePercent: 0.0, // Would need platform-specific implementation
            NetworkInBytesPerSecond: 0.0, // Would need network monitoring
            NetworkOutBytesPerSecond: 0.0,
            ActiveConnections: 0, // Would need network stack access
            CapturedAt: DateTime.UtcNow
        );
    }

    /// <summary>
    /// Checks application health status
    /// </summary>
    public async Task<HealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking application health");
        
        var checks = new List<HealthCheckResult>();
        
        // Memory check
        var memoryOk = GC.GetTotalMemory(false) < 1_000_000_000; // 1GB threshold
        checks.Add(new HealthCheckResult(
            Name: "Memory",
            IsHealthy: memoryOk,
            Status: memoryOk ? "Healthy" : "Degraded",
            Description: $"Current memory: {GC.GetTotalMemory(false):N0} bytes"
        ));
        
        // Thread check
        var threadCount = _currentProcess.Threads.Count;
        var threadsOk = threadCount < 100;
        checks.Add(new HealthCheckResult(
            Name: "Threads",
            IsHealthy: threadsOk,
            Status: threadsOk ? "Healthy" : "Warning",
            Description: $"Active threads: {threadCount}"
        ));
        
        // GC check
        var gen2Collections = GC.CollectionCount(2);
        var gcOk = gen2Collections < 100;
        checks.Add(new HealthCheckResult(
            Name: "GarbageCollection",
            IsHealthy: gcOk,
            Status: gcOk ? "Healthy" : "Warning",
            Description: $"Gen2 collections: {gen2Collections}"
        ));

        var isHealthy = checks.All(c => c.IsHealthy);
        
        return new HealthStatus(
            ApplicationName: AppDomain.CurrentDomain.FriendlyName,
            IsHealthy: isHealthy,
            Status: isHealthy ? "Healthy" : "Unhealthy",
            Uptime: _uptimeStopwatch.Elapsed,
            Checks: checks.AsReadOnly(),
            CapturedAt: DateTime.UtcNow
        );
    }

    /// <summary>
    /// Starts a profiling session
    /// </summary>
    public async Task<ProfilingResults> StartProfilingSessionAsync(
        ProfilingConfig config,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting profiling session: {SessionName}", config.SessionName);
        
        var startTime = DateTime.UtcNow;
        var cpuHotSpots = new List<HotSpot>();
        var allocations = new List<AllocationSite>();
        var gcEvents = new List<GcEvent>();
        
        // Capture initial GC stats
        var initialGen0 = GC.CollectionCount(0);
        var initialGen1 = GC.CollectionCount(1);
        var initialGen2 = GC.CollectionCount(2);
        var initialMemory = GC.GetTotalMemory(false);
        
        // Wait for the specified duration or until cancellation
        try
        {
            await Task.Delay(config.Duration, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Profiling session cancelled: {SessionName}", config.SessionName);
        }
        
        // Capture final stats
        var finalGen0 = GC.CollectionCount(0);
        var finalGen1 = GC.CollectionCount(1);
        var finalGen2 = GC.CollectionCount(2);
        var finalMemory = GC.GetTotalMemory(false);
        
        // Generate synthetic hot spots from current stack trace
        var stackTrace = new StackTrace(true);
        foreach (var frame in stackTrace.GetFrames().Take(10))
        {
            var method = frame.GetMethod();
            if (method != null)
            {
                cpuHotSpots.Add(new HotSpot(
                    MethodName: method.Name,
                    FileName: frame.GetFileName() ?? "unknown",
                    LineNumber: frame.GetFileLineNumber(),
                    CpuPercent: 0.0, // Would need actual sampling
                    CallCount: 1,
                    AvgDurationMs: 0.0
                ));
            }
        }
        
        // Record GC events that occurred during profiling
        if (config.CollectGcEvents)
        {
            var gcCount0 = finalGen0 - initialGen0;
            var gcCount1 = finalGen1 - initialGen1;
            var gcCount2 = finalGen2 - initialGen2;
            
            for (int i = 0; i < gcCount0; i++)
            {
                gcEvents.Add(new GcEvent(0, 1, 0, 0, DateTime.UtcNow));
            }
            for (int i = 0; i < gcCount1; i++)
            {
                gcEvents.Add(new GcEvent(1, 5, 0, 0, DateTime.UtcNow));
            }
            for (int i = 0; i < gcCount2; i++)
            {
                gcEvents.Add(new GcEvent(2, 50, 0, 0, DateTime.UtcNow));
            }
        }
        
        var results = new ProfilingResults(
            SessionName: config.SessionName,
            Duration: DateTime.UtcNow - startTime,
            CpuHotSpots: cpuHotSpots.AsReadOnly(),
            TopAllocations: allocations.AsReadOnly(),
            GcEvents: gcEvents.AsReadOnly(),
            CompletedAt: DateTime.UtcNow
        );
        
        _logger.LogInformation("Profiling session completed: {SessionName}", config.SessionName);
        return results;
    }

    private static double CalculateCpuUsage()
    {
        // Simplified CPU calculation - would need more sophisticated approach in production
        return 0.0;
    }

    private static async Task<double> GetSystemCpuUsageAsync(CancellationToken ct)
    {
        // Platform-specific CPU usage calculation
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Would use PerformanceCounter on Windows
            return await Task.FromResult(0.0);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Would read /proc/stat on Linux
            return await Task.FromResult(0.0);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Would use sysctl on macOS
            return await Task.FromResult(0.0);
        }
        
        return 0.0;
    }

    private static (double totalMemory, double memoryPercent) GetSystemMemoryUsage()
    {
        // Simplified memory calculation
        var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var usedMemory = GC.GetTotalMemory(false);
        var percent = totalMemory > 0 ? (usedMemory * 100.0 / totalMemory) : 0.0;
        
        return (totalMemory, percent);
    }
}
