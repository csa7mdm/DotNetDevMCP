// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;

namespace DotNetDevMCP.Core.Interfaces;

/// <summary>
/// Manages resources for concurrent operations with throttling
/// </summary>
public interface IResourceManager
{
    /// <summary>
    /// Gets or sets the maximum number of concurrent operations
    /// </summary>
    int MaxConcurrency { get; set; }

    /// <summary>
    /// Gets the current number of executing operations
    /// </summary>
    int CurrentlyExecuting { get; }

    /// <summary>
    /// Gets the number of operations queued waiting for execution
    /// </summary>
    int QueuedOperations { get; }

    /// <summary>
    /// Executes an operation with throttling to respect max concurrency
    /// </summary>
    Task<T> ExecuteWithThrottlingAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a batch of operations with throttling
    /// </summary>
    Task<IEnumerable<T>> ExecuteBatchWithThrottlingAsync<T>(
        IEnumerable<Func<Task<T>>> operations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current resource usage metrics
    /// </summary>
    ResourceMetrics GetMetrics();
}
