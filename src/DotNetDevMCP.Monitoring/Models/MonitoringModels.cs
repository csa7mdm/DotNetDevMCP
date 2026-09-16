namespace DotNetDevMCP.Monitoring.Models;

/// <summary>
/// Represents performance metrics for a process or application
/// </summary>
public sealed record PerformanceMetrics(
    string ProcessName,
    int ProcessId,
    double CpuUsagePercent,
    long MemoryUsageBytes,
    long PeakMemoryUsageBytes,
    int ThreadCount,
    int HandleCount,
    double GcHeapSizeBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    DateTime CapturedAt
);

/// <summary>
/// Represents application health status
/// </summary>
public sealed record HealthStatus(
    string ApplicationName,
    bool IsHealthy,
    string Status,
    TimeSpan Uptime,
    IReadOnlyList<HealthCheckResult> Checks,
    DateTime CapturedAt
);

/// <summary>
/// Represents a single health check result
/// </summary>
public sealed record HealthCheckResult(
    string Name,
    bool IsHealthy,
    string Status,
    string? Description = null,
    TimeSpan? Duration = null,
    Exception? Exception = null
);

/// <summary>
/// Represents resource utilization metrics
/// </summary>
public sealed record ResourceUtilization(
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double DiskUsagePercent,
    double NetworkInBytesPerSecond,
    double NetworkOutBytesPerSecond,
    int ActiveConnections,
    DateTime CapturedAt
);

/// <summary>
/// Represents profiling session configuration
/// </summary>
public sealed record ProfilingConfig(
    string SessionName,
    TimeSpan Duration,
    bool CollectCpuSamples = true,
    bool CollectMemorySamples = true,
    bool CollectGcEvents = true,
    bool CollectThreadEvents = true,
    int SamplingIntervalMs = 100
);

/// <summary>
/// Represents profiling results summary
/// </summary>
public sealed record ProfilingResults(
    string SessionName,
    TimeSpan Duration,
    IReadOnlyList<HotSpot> CpuHotSpots,
    IReadOnlyList<AllocationSite> TopAllocations,
    IReadOnlyList<GcEvent> GcEvents,
    DateTime CompletedAt
);

/// <summary>
/// Represents a CPU hot spot in profiling results
/// </summary>
public sealed record HotSpot(
    string MethodName,
    string FileName,
    int LineNumber,
    double CpuPercent,
    int CallCount,
    double AvgDurationMs
);

/// <summary>
/// Represents a memory allocation site
/// </summary>
public sealed record AllocationSite(
    string TypeName,
    string MethodName,
    long TotalBytesAllocated,
    int AllocationCount,
    double AvgBytesPerAllocation
);

/// <summary>
/// Represents a garbage collection event
/// </summary>
public sealed record GcEvent(
    int Generation,
    long DurationMs,
    long BytesAllocated,
    long BytesPromoted,
    DateTime Timestamp
);
