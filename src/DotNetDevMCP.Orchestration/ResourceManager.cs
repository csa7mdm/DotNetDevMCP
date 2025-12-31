// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using System.Diagnostics;

namespace DotNetDevMCP.Orchestration;

/// <summary>
/// Manages resources and throttles concurrent operations to prevent exhaustion
/// </summary>
public class ResourceManager : IResourceManager, IDisposable
{
    private SemaphoreSlim _semaphore;
    private int _maxConcurrency;
    private int _currentlyExecuting;
    private int _totalExecuted;
    private int _totalFailed;
    private readonly List<TimeSpan> _executionTimes = new();
    private readonly object _lockObj = new();

    public ResourceManager(int maxConcurrency = 0)
    {
        if (maxConcurrency <= 0)
        {
            // Default to processor count if not specified or invalid
            maxConcurrency = Environment.ProcessorCount;
        }

        _maxConcurrency = maxConcurrency;
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <inheritdoc />
    public int MaxConcurrency
    {
        get => _maxConcurrency;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "MaxConcurrency must be greater than 0");

            lock (_lockObj)
            {
                if (value != _maxConcurrency)
                {
                    var oldSemaphore = _semaphore;
                    _semaphore = new SemaphoreSlim(value, value);
                    _maxConcurrency = value;

                    // Dispose old semaphore immediately - operations already holding the semaphore
                    // will complete normally, and new operations will use the new semaphore
                    oldSemaphore.Dispose();
                }
            }
        }
    }

    /// <inheritdoc />
    public int CurrentlyExecuting => _currentlyExecuting;

    /// <inheritdoc />
    public int QueuedOperations => _maxConcurrency - _semaphore.CurrentCount - _currentlyExecuting;

    /// <inheritdoc />
    public async Task<T> ExecuteWithThrottlingAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        Interlocked.Increment(ref _currentlyExecuting);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await operation();

            stopwatch.Stop();
            lock (_lockObj)
            {
                _totalExecuted++;
                _executionTimes.Add(stopwatch.Elapsed);
            }

            return result;
        }
        catch
        {
            stopwatch.Stop();
            lock (_lockObj)
            {
                _totalExecuted++;
                _totalFailed++;
                _executionTimes.Add(stopwatch.Elapsed);
            }
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _currentlyExecuting);
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<T>> ExecuteBatchWithThrottlingAsync<T>(
        IEnumerable<Func<Task<T>>> operations,
        CancellationToken cancellationToken = default)
    {
        var operationsList = operations.ToList();
        var results = new T[operationsList.Count];
        var tasks = new List<Task>();

        for (int i = 0; i < operationsList.Count; i++)
        {
            var index = i; // Capture for closure
            var operation = operationsList[i];

            var task = Task.Run(async () =>
            {
                var result = await ExecuteWithThrottlingAsync(operation, cancellationToken);
                results[index] = result;
            }, cancellationToken);

            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
        return results;
    }

    /// <inheritdoc />
    public ResourceMetrics GetMetrics()
    {
        lock (_lockObj)
        {
            var avgExecutionTime = _executionTimes.Any()
                ? TimeSpan.FromMilliseconds(_executionTimes.Average(t => t.TotalMilliseconds))
                : TimeSpan.Zero;

            return new ResourceMetrics(
                MaxConcurrency: _maxConcurrency,
                CurrentlyExecuting: _currentlyExecuting,
                TotalExecuted: _totalExecuted,
                TotalFailed: _totalFailed,
                AverageExecutionTime: avgExecutionTime
            );
        }
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}
