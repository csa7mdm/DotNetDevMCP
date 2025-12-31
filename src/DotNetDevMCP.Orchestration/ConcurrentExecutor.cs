// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DotNetDevMCP.Orchestration;

/// <summary>
/// Executes multiple operations concurrently with error aggregation and progress reporting
/// </summary>
public class ConcurrentExecutor : IConcurrentExecutor
{
    /// <inheritdoc />
    public async Task<ConcurrentExecutionResult<T>> ExecuteAsync<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations,
        ConcurrentExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(operations, options, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ConcurrentExecutionResult<T>> ExecuteAsync<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations,
        IProgress<ExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var defaultOptions = new ConcurrentExecutionOptions();
        return await ExecuteAsync(operations, defaultOptions, progress, cancellationToken);
    }

    private async Task<ConcurrentExecutionResult<T>> ExecuteAsync<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations,
        ConcurrentExecutionOptions options,
        IProgress<ExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var operationsList = operations.ToList();
        var totalOperations = operationsList.Count;

        if (totalOperations == 0)
        {
            return new ConcurrentExecutionResult<T>(
                SuccessfulResults: Array.Empty<T>(),
                Errors: Array.Empty<ExecutionError>(),
                TotalOperations: 0,
                SuccessfulOperations: 0,
                Duration: TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        var successfulResults = new ConcurrentBag<(int index, T result)>();
        var errors = new ConcurrentBag<ExecutionError>();
        var completedCount = 0;
        var failedCount = 0;

        try
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism ?? Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(
                operationsList.Select((op, index) => (op, index)),
                parallelOptions,
                async (item, ct) =>
                {
                    var (operation, index) = item;

                    try
                    {
                        // Apply timeout if specified
                        T result;
                        if (options.OperationTimeout.HasValue)
                        {
                            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            timeoutCts.CancelAfter(options.OperationTimeout.Value);

                            result = await operation(timeoutCts.Token);
                        }
                        else
                        {
                            result = await operation(ct);
                        }

                        successfulResults.Add((index, result));
                        Interlocked.Increment(ref completedCount);

                        ReportProgress(progress, totalOperations, completedCount, failedCount);
                    }
                    catch (Exception ex)
                    {
                        var error = new ExecutionError(
                            OperationIndex: index,
                            Exception: ex,
                            Message: ex.Message);

                        errors.Add(error);
                        Interlocked.Increment(ref completedCount);
                        Interlocked.Increment(ref failedCount);

                        ReportProgress(progress, totalOperations, completedCount, failedCount);

                        if (!options.ContinueOnError)
                        {
                            throw;
                        }
                    }
                });

            stopwatch.Stop();

            // Sort successful results by original index to maintain order
            var sortedResults = successfulResults
                .OrderBy(r => r.index)
                .Select(r => r.result)
                .ToList();

            return new ConcurrentExecutionResult<T>(
                SuccessfulResults: sortedResults,
                Errors: errors.OrderBy(e => e.OperationIndex).ToList(),
                TotalOperations: totalOperations,
                SuccessfulOperations: successfulResults.Count,
                Duration: stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            throw;
        }
        catch when (options.ContinueOnError)
        {
            // If ContinueOnError is true, we shouldn't reach here
            // but if we do, return partial results
            stopwatch.Stop();

            var sortedResults = successfulResults
                .OrderBy(r => r.index)
                .Select(r => r.result)
                .ToList();

            return new ConcurrentExecutionResult<T>(
                SuccessfulResults: sortedResults,
                Errors: errors.OrderBy(e => e.OperationIndex).ToList(),
                TotalOperations: totalOperations,
                SuccessfulOperations: successfulResults.Count,
                Duration: stopwatch.Elapsed);
        }
    }

    private static void ReportProgress(
        IProgress<ExecutionProgress>? progress,
        int totalOperations,
        int completedOperations,
        int failedOperations)
    {
        progress?.Report(new ExecutionProgress(
            TotalOperations: totalOperations,
            CompletedOperations: completedOperations,
            FailedOperations: failedOperations));
    }
}
