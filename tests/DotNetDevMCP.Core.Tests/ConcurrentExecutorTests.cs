// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;
using DotNetDevMCP.Orchestration;
using FluentAssertions;
using Xunit;

namespace DotNetDevMCP.Core.Tests;

/// <summary>
/// Tests for ConcurrentExecutor - ensures parallel execution with error handling
/// </summary>
public class ConcurrentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WithSuccessfulOperations_ReturnsAllResults()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var operations = Enumerable.Range(0, 5)
            .Select<int, Func<CancellationToken, Task<int>>>(i => ct => Task.FromResult(i * 2));

        // Act
        var result = await executor.ExecuteAsync(
            operations,
            new ConcurrentExecutionOptions());

        // Assert
        result.SuccessfulResults.Should().BeEquivalentTo(new[] { 0, 2, 4, 6, 8 });
        result.TotalOperations.Should().Be(5);
        result.SuccessfulOperations.Should().Be(5);
        result.Errors.Should().BeEmpty();
        result.AllSucceeded.Should().BeTrue();
        result.SuccessRate.Should().Be(1.0);
    }

    [Fact]
    public async Task ExecuteAsync_WithMixedSuccessAndFailure_ReturnsPartialResults()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var operations = Enumerable.Range(0, 5)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                await Task.Delay(10, ct);
                if (i % 2 == 0)
                    throw new InvalidOperationException($"Operation {i} failed");
                return i * 2;
            });

        var options = new ConcurrentExecutionOptions(ContinueOnError: true);

        // Act
        var result = await executor.ExecuteAsync(operations, options);

        // Assert
        result.SuccessfulResults.Should().BeEquivalentTo(new[] { 2, 6 }); // indices 1, 3
        result.TotalOperations.Should().Be(5);
        result.SuccessfulOperations.Should().Be(2);
        result.Errors.Should().HaveCount(3); // indices 0, 2, 4
        result.AllSucceeded.Should().BeFalse();
        result.HasErrors.Should().BeTrue();
        result.SuccessRate.Should().Be(0.4); // 2/5
    }

    [Fact]
    public async Task ExecuteAsync_WithContinueOnErrorFalse_StopsOnFirstError()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var executedIndices = new List<int>();
        var lockObj = new object();

        var operations = Enumerable.Range(0, 10)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                await Task.Delay(50, ct);
                lock (lockObj) { executedIndices.Add(i); }

                if (i == 3)
                    throw new InvalidOperationException("Intentional failure");

                return i;
            });

        var options = new ConcurrentExecutionOptions(ContinueOnError: false);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.ExecuteAsync(operations, options));
    }

    [Fact]
    public async Task ExecuteAsync_RespectsMaxDegreeOfParallelism()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var currentlyExecuting = 0;
        var maxObserved = 0;
        var lockObj = new object();

        var operations = Enumerable.Range(0, 20)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                lock (lockObj)
                {
                    currentlyExecuting++;
                    maxObserved = Math.Max(maxObserved, currentlyExecuting);
                }

                await Task.Delay(100, ct);

                lock (lockObj)
                {
                    currentlyExecuting--;
                }

                return i;
            });

        var options = new ConcurrentExecutionOptions(MaxDegreeOfParallelism: 3);

        // Act
        var result = await executor.ExecuteAsync(operations, options);

        // Assert
        result.AllSucceeded.Should().BeTrue();
        maxObserved.Should().BeLessThanOrEqualTo(3, "max parallelism should be respected");
    }

    [Fact]
    public async Task ExecuteAsync_WithProgress_ReportsProgress()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var progressReports = new List<ExecutionProgress>();
        var lockObj = new object();
        var progress = new Progress<ExecutionProgress>(p =>
        {
            lock (lockObj)
            {
                progressReports.Add(p);
            }
        });

        var operations = Enumerable.Range(0, 10)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                await Task.Delay(20, ct);
                return i;
            });

        // Act
        var result = await executor.ExecuteAsync(operations, progress);

        // Small delay to ensure all progress reports are captured
        await Task.Delay(100);

        // Assert
        result.AllSucceeded.Should().BeTrue();

        lock (lockObj)
        {
            progressReports.Should().NotBeEmpty("progress should be reported");

            // Check that progress was reported - should have completion report
            var maxCompleted = progressReports.Max(p => p.CompletedOperations);
            maxCompleted.Should().Be(10, "all operations should eventually be reported as complete");

            // Check that we got a 100% progress report
            progressReports.Should().Contain(p => p.PercentComplete == 100.0,
                "should report 100% completion");
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithTimeout_TimesOutLongOperations()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var operations = Enumerable.Range(0, 5)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                if (i == 2)
                {
                    await Task.Delay(5000, ct); // Long operation
                }
                else
                {
                    await Task.Delay(10, ct);
                }
                return i;
            });

        var options = new ConcurrentExecutionOptions(
            OperationTimeout: TimeSpan.FromMilliseconds(100),
            ContinueOnError: true);

        // Act
        var result = await executor.ExecuteAsync(operations, options);

        // Assert
        result.SuccessfulOperations.Should().Be(4); // All except index 2
        result.Errors.Should().HaveCount(1);
        result.Errors.First().OperationIndex.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_SupportsCancellation()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        using var cts = new CancellationTokenSource();

        var operations = Enumerable.Range(0, 10)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                await Task.Delay(2000, ct);
                return i;
            });

        var options = new ConcurrentExecutionOptions();

        // Act
        var task = executor.ExecuteAsync(operations, options, cts.Token);
        cts.CancelAfter(100);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task ExecuteAsync_CapturesExceptionDetails()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var operations = Enumerable.Range(0, 3)
            .Select<int, Func<CancellationToken, Task<int>>>(i => ct =>
            {
                if (i == 1)
                    throw new ArgumentException($"Argument error for {i}");
                return Task.FromResult(i);
            });

        var options = new ConcurrentExecutionOptions(ContinueOnError: true);

        // Act
        var result = await executor.ExecuteAsync(operations, options);

        // Assert
        result.Errors.Should().HaveCount(1);
        var error = result.Errors.First();
        error.OperationIndex.Should().Be(1);
        error.Exception.Should().BeOfType<ArgumentException>();
        error.Message.Should().Contain("Argument error");
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyOperations_ReturnsEmptyResult()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var operations = Enumerable.Empty<Func<CancellationToken, Task<int>>>();

        // Act
        var result = await executor.ExecuteAsync(
            operations,
            new ConcurrentExecutionOptions());

        // Assert
        result.TotalOperations.Should().Be(0);
        result.SuccessfulOperations.Should().Be(0);
        result.SuccessfulResults.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
        result.AllSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_MeasuresDuration()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var operations = Enumerable.Range(0, 5)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                await Task.Delay(100, ct);
                return i;
            });

        // Act
        var result = await executor.ExecuteAsync(
            operations,
            new ConcurrentExecutionOptions());

        // Assert
        result.Duration.Should().BeGreaterThan(TimeSpan.FromMilliseconds(50));
        result.Duration.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ExecuteAsync_HandlesHighConcurrency()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var operationCount = 100;

        var operations = Enumerable.Range(0, operationCount)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                await Task.Delay(Random.Shared.Next(1, 10), ct);
                return i * 2;
            });

        var options = new ConcurrentExecutionOptions(MaxDegreeOfParallelism: 20);

        // Act
        var result = await executor.ExecuteAsync(operations, options);

        // Assert
        result.TotalOperations.Should().Be(operationCount);
        result.SuccessfulOperations.Should().Be(operationCount);
        result.SuccessfulResults.Should().HaveCount(operationCount);
        result.AllSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithProgressAndErrors_ReportsCorrectly()
    {
        // Arrange
        var executor = new ConcurrentExecutor();
        var progressReports = new List<ExecutionProgress>();
        var progress = new Progress<ExecutionProgress>(p => progressReports.Add(p));

        var operations = Enumerable.Range(0, 10)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                await Task.Delay(20, ct);
                if (i % 3 == 0)
                    throw new Exception($"Error {i}");
                return i;
            });

        // Act
        var result = await executor.ExecuteAsync(operations, progress);

        // Assert
        result.SuccessfulOperations.Should().Be(6);
        result.Errors.Should().HaveCount(4); // indices 0, 3, 6, 9

        var lastProgress = progressReports.Last();
        lastProgress.CompletedOperations.Should().Be(10);
        lastProgress.FailedOperations.Should().Be(4);
    }
}
