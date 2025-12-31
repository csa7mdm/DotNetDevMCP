// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using DotNetDevMCP.Orchestration;
using FluentAssertions;
using Xunit;

namespace DotNetDevMCP.Core.Tests;

/// <summary>
/// Tests for OrchestrationService - ensures proper coordination of all orchestration components
/// </summary>
public class OrchestrationServiceTests
{
    [Fact]
    public void Constructor_InitializesAllComponents()
    {
        // Act
        var service = new OrchestrationService();

        // Assert
        service.ResourceManager.Should().NotBeNull();
        service.ResourceManager.MaxConcurrency.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteParallelAsync_WithSimpleOperations_ExecutesSuccessfully()
    {
        // Arrange
        var service = new OrchestrationService();
        var executedOperations = new List<string>();
        var lockObj = new object();

        // Simulate tool operations
        var operations = new[]
        {
            ("tool1", "arg1"),
            ("tool2", "arg2"),
            ("tool3", "arg3")
        };

        // Mock tool resolver
        service.RegisterTool("tool1", async (args, ct) =>
        {
            lock (lockObj) { executedOperations.Add($"tool1:{args}"); }
            await Task.Delay(10, ct);
            return ToolResult.Success($"tool1 executed with {args}");
        });

        service.RegisterTool("tool2", async (args, ct) =>
        {
            lock (lockObj) { executedOperations.Add($"tool2:{args}"); }
            await Task.Delay(10, ct);
            return ToolResult.Success($"tool2 executed with {args}");
        });

        service.RegisterTool("tool3", async (args, ct) =>
        {
            lock (lockObj) { executedOperations.Add($"tool3:{args}"); }
            await Task.Delay(10, ct);
            return ToolResult.Success($"tool3 executed with {args}");
        });

        // Act
        var results = await service.ExecuteParallelAsync(operations);

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        executedOperations.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExecuteParallelAsync_WithMixedResults_ReturnsAllResults()
    {
        // Arrange
        var service = new OrchestrationService();

        service.RegisterTool("success", async (args, ct) =>
        {
            await Task.Delay(10, ct);
            return ToolResult.Success("Success result");
        });

        service.RegisterTool("failure", async (args, ct) =>
        {
            await Task.Delay(10, ct);
            return ToolResult.Failure("Intentional failure");
        });

        var operations = new[]
        {
            ("success", "args1"),
            ("failure", "args2"),
            ("success", "args3")
        };

        // Act
        var results = await service.ExecuteParallelAsync(operations);

        // Assert
        results.Should().HaveCount(3);
        results.Count(r => r.IsSuccess).Should().Be(2);
        results.Count(r => !r.IsSuccess).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteParallelAsync_RespectsResourceManager()
    {
        // Arrange
        var service = new OrchestrationService();
        service.ResourceManager.MaxConcurrency = 2;

        var currentlyExecuting = 0;
        var maxObserved = 0;
        var lockObj = new object();

        service.RegisterTool("delayTool", async (args, ct) =>
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

            return ToolResult.Success("Done");
        });

        var operations = Enumerable.Range(0, 10)
            .Select(i => ("delayTool", $"arg{i}"))
            .ToArray();

        // Act
        var results = await service.ExecuteParallelAsync(operations);

        // Assert
        results.Should().HaveCount(10);
        maxObserved.Should().BeLessThanOrEqualTo(2, "should respect max concurrency");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithSimpleWorkflow_ExecutesSuccessfully()
    {
        // Arrange
        var service = new OrchestrationService();
        var executionOrder = new List<string>();
        var lockObj = new object();

        var workflow = new TestWorkflow("SimpleWorkflow")
            .AddStep("Step1", async (ctx, ct) =>
            {
                lock (lockObj) { executionOrder.Add("Step1"); }
                ctx.Set("Step1Data", "Value1");
                await Task.Delay(10, ct);
                return new StepResult(true);
            })
            .AddStep("Step2", async (ctx, ct) =>
            {
                lock (lockObj) { executionOrder.Add("Step2"); }
                var data = ctx.Get<string>("Step1Data");
                data.Should().Be("Value1");
                await Task.Delay(10, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Step1" });

        // Act
        var result = await service.ExecuteWorkflowAsync(workflow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Content.Should().NotBeNullOrEmpty();
        executionOrder.Should().ContainInOrder("Step1", "Step2");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithFailedStep_ReturnsFailure()
    {
        // Arrange
        var service = new OrchestrationService();

        var workflow = new TestWorkflow("FailingWorkflow")
            .AddStep("SuccessStep", async (ctx, ct) =>
            {
                await Task.CompletedTask;
                return new StepResult(true);
            })
            .AddStep("FailingStep", async (ctx, ct) =>
            {
                await Task.CompletedTask;
                return new StepResult(false, "Step failed");
            }, dependsOn: new[] { "SuccessStep" });

        // Act
        var result = await service.ExecuteWorkflowAsync(workflow);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("failed");
    }

    [Fact]
    public async Task ExecuteParallelAsync_WithUnknownTool_ReturnsError()
    {
        // Arrange
        var service = new OrchestrationService();

        var operations = new[]
        {
            ("unknownTool", "args")
        };

        // Act
        var results = await service.ExecuteParallelAsync(operations);

        // Assert
        results.Should().HaveCount(1);
        results.First().IsSuccess.Should().BeFalse();
        results.First().Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteParallelAsync_SupportsCancellation()
    {
        // Arrange
        var service = new OrchestrationService();
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>();

        service.RegisterTool("longTool", async (args, ct) =>
        {
            started.SetResult(true);
            await Task.Delay(10000, ct);
            return ToolResult.Success("Done");
        });

        var operations = new[] { ("longTool", "args") };

        // Act
        var task = service.ExecuteParallelAsync(operations, cts.Token);
        await started.Task;
        cts.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public void ResourceManager_CanBeAccessed()
    {
        // Arrange
        var service = new OrchestrationService();

        // Act
        var initialConcurrency = service.ResourceManager.MaxConcurrency;
        service.ResourceManager.MaxConcurrency = 10;

        // Assert
        initialConcurrency.Should().BeGreaterThan(0);
        service.ResourceManager.MaxConcurrency.Should().Be(10);
    }

    [Fact]
    public async Task ExecuteParallelAsync_WithEmptyOperations_ReturnsEmpty()
    {
        // Arrange
        var service = new OrchestrationService();
        var operations = Array.Empty<(string toolName, string arguments)>();

        // Act
        var results = await service.ExecuteParallelAsync(operations);

        // Assert
        results.Should().BeEmpty();
    }
}
