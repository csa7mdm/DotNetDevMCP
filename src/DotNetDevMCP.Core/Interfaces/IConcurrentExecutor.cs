// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;

namespace DotNetDevMCP.Core.Interfaces;

/// <summary>
/// Executes multiple operations concurrently with error aggregation and progress reporting
/// </summary>
public interface IConcurrentExecutor
{
    /// <summary>
    /// Executes multiple operations concurrently with configurable options
    /// </summary>
    Task<ConcurrentExecutionResult<T>> ExecuteAsync<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations,
        ConcurrentExecutionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes multiple operations concurrently with progress reporting
    /// </summary>
    Task<ConcurrentExecutionResult<T>> ExecuteAsync<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations,
        IProgress<ExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
