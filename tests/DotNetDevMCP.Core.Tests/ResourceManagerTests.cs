// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Orchestration;
using FluentAssertions;
using Xunit;

namespace DotNetDevMCP.Core.Tests;

/// <summary>
/// Tests for ResourceManager - ensures proper throttling and resource management
/// </summary>
public class ResourceManagerTests
{
    [Fact]
    public async Task ExecuteWithThrottlingAsync_SingleOperation_Succeeds()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 4);
        var operationExecuted = false;

        // Act
        var result = await resourceManager.ExecuteWithThrottlingAsync(async () =>
        {
            await Task.Delay(10);
            operationExecuted = true;
            return "Success";
        });

        // Assert
        result.Should().Be("Success");
        operationExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWithThrottlingAsync_RespectsMaxConcurrency()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 2);
        var currentlyExecuting = 0;
        var maxObservedConcurrency = 0;
        var lockObj = new object();

        var operations = Enumerable.Range(0, 10).Select<int, Func<Task<string>>>(_ => async () =>
        {
            lock (lockObj)
            {
                currentlyExecuting++;
                maxObservedConcurrency = Math.Max(maxObservedConcurrency, currentlyExecuting);
            }

            await Task.Delay(50); // Simulate work

            lock (lockObj)
            {
                currentlyExecuting--;
            }

            return "Done";
        });

        // Act
        var tasks = operations.Select(op => resourceManager.ExecuteWithThrottlingAsync(op));
        await Task.WhenAll(tasks);

        // Assert
        maxObservedConcurrency.Should().BeLessThanOrEqualTo(2, "max concurrency should be respected");
    }

    [Fact]
    public async Task ExecuteWithThrottlingAsync_ThrottlesCorrectly()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 3);
        var executionTimes = new List<DateTime>();
        var lockObj = new object();

        var operations = Enumerable.Range(0, 6).Select<int, Func<Task<int>>>(i => async () =>
        {
            lock (lockObj)
            {
                executionTimes.Add(DateTime.UtcNow);
            }

            await Task.Delay(100);
            return i;
        });

        // Act
        var startTime = DateTime.UtcNow;
        var tasks = operations.Select(op => resourceManager.ExecuteWithThrottlingAsync(op));
        await Task.WhenAll(tasks);
        var duration = DateTime.UtcNow - startTime;

        // Assert
        // With max concurrency of 3 and 6 operations taking 100ms each,
        // we should see 2 batches: first 3 start immediately, next 3 wait
        duration.Should().BeGreaterThan(TimeSpan.FromMilliseconds(150),
            "operations should be throttled into batches");

        // First 3 should start within a short window
        var firstBatch = executionTimes.OrderBy(t => t).Take(3).ToList();
        var firstBatchSpan = firstBatch.Last() - firstBatch.First();
        firstBatchSpan.Should().BeLessThan(TimeSpan.FromMilliseconds(50),
            "first batch should start nearly simultaneously");
    }

    [Fact]
    public async Task ExecuteWithThrottlingAsync_PropagatesExceptions()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 4);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await resourceManager.ExecuteWithThrottlingAsync<int>(async () =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("Test exception");
            });
        });
    }

    [Fact]
    public async Task ExecuteWithThrottlingAsync_SupportsCancellation()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 2);
        using var cts = new CancellationTokenSource();

        // Act
        var task = resourceManager.ExecuteWithThrottlingAsync(async () =>
        {
            await Task.Delay(5000, cts.Token);
            return "Should not complete";
        }, cts.Token);

        cts.CancelAfter(100);

        // Assert (TaskCanceledException inherits from OperationCanceledException)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task ExecuteBatchWithThrottlingAsync_ExecutesAllOperations()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 3);
        var executedIndices = new List<int>();
        var lockObj = new object();

        var operations = Enumerable.Range(0, 10).Select(i => new Func<Task<int>>(async () =>
        {
            await Task.Delay(10);
            lock (lockObj)
            {
                executedIndices.Add(i);
            }
            return i;
        })).ToList();

        // Act
        var results = await resourceManager.ExecuteBatchWithThrottlingAsync(operations);

        // Assert
        results.Should().HaveCount(10);
        results.Should().BeEquivalentTo(Enumerable.Range(0, 10));
        executedIndices.Should().HaveCount(10);
    }

    [Fact]
    public void MaxConcurrency_CanBeUpdated()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 4);

        // Act
        resourceManager.MaxConcurrency = 8;

        // Assert
        resourceManager.MaxConcurrency.Should().Be(8);
    }

    [Fact]
    public void MaxConcurrency_ThrowsOnInvalidValue()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 4);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => resourceManager.MaxConcurrency = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => resourceManager.MaxConcurrency = -1);
    }

    [Fact]
    public async Task GetMetrics_ReturnsAccurateData()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 2);

        // Act - Execute some operations
        var operations = Enumerable.Range(0, 5).Select<int, Func<Task<int>>>(i => async () =>
        {
            await Task.Delay(10);
            return i;
        });

        await resourceManager.ExecuteBatchWithThrottlingAsync(operations);

        var metrics = resourceManager.GetMetrics();

        // Assert
        metrics.MaxConcurrency.Should().Be(2);
        metrics.TotalExecuted.Should().Be(5);
        metrics.TotalFailed.Should().Be(0);
        metrics.CurrentlyExecuting.Should().Be(0);
    }

    [Fact]
    public async Task GetMetrics_TracksFailures()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 4);

        // Act - Execute operations with some failures
        var operations = Enumerable.Range(0, 5).Select(i => new Func<Task<int>>(async () =>
        {
            await Task.Delay(10);
            if (i % 2 == 0)
                throw new Exception("Test failure");
            return i;
        })).ToList();

        var tasks = operations.Select(op => resourceManager.ExecuteWithThrottlingAsync(op));

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Expected
        }

        var metrics = resourceManager.GetMetrics();

        // Assert
        metrics.TotalExecuted.Should().Be(5);
        metrics.TotalFailed.Should().Be(3); // indices 0, 2, 4 should fail
    }

    [Fact]
    public async Task ResourceManager_HandlesHighConcurrency()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 20);
        var operationCount = 100;

        var operations = Enumerable.Range(0, operationCount).Select<int, Func<Task<int>>>(i => async () =>
        {
            await Task.Delay(Random.Shared.Next(1, 50));
            return i * 2;
        });

        // Act
        var startTime = DateTime.UtcNow;
        var results = await resourceManager.ExecuteBatchWithThrottlingAsync(operations);
        var duration = DateTime.UtcNow - startTime;

        // Assert
        results.Should().HaveCount(operationCount);
        results.Should().BeEquivalentTo(Enumerable.Range(0, operationCount).Select(i => i * 2));

        // With max concurrency of 20, should be much faster than sequential
        Console.WriteLine($"Executed {operationCount} operations in {duration.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task CurrentlyExecuting_ReflectsActiveOperations()
    {
        // Arrange
        var resourceManager = new ResourceManager(maxConcurrency: 3);
        var barrier = new SemaphoreSlim(0, 3);
        var releaseBarrier = new SemaphoreSlim(0, 1);

        // Act - Start long-running operations
        var operations = Enumerable.Range(0, 3).Select<int, Task>(i => Task.Run(async () =>
        {
            await resourceManager.ExecuteWithThrottlingAsync(async () =>
            {
                barrier.Release(); // Signal that this operation has started
                await releaseBarrier.WaitAsync(); // Wait for release signal
                return i;
            });
        }));

        // Wait for all operations to start
        for (int i = 0; i < 3; i++)
        {
            await barrier.WaitAsync();
        }

        var countDuringExecution = resourceManager.CurrentlyExecuting;

        releaseBarrier.Release(3); // Release all operations
        await Task.WhenAll(operations);

        var countAfterCompletion = resourceManager.CurrentlyExecuting;

        // Assert
        countDuringExecution.Should().Be(3, "all 3 operations should be executing");
        countAfterCompletion.Should().Be(0, "no operations should be executing after completion");
    }
}
