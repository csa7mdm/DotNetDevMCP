// Copyright (c) 2025 Ahmed Mustafa

namespace DotNetDevMCP.Core.Models;

/// <summary>
/// Resource usage metrics for orchestration operations
/// </summary>
public record ResourceMetrics(
    int MaxConcurrency,
    int CurrentlyExecuting,
    int TotalExecuted,
    int TotalFailed,
    TimeSpan AverageExecutionTime);
