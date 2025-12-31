// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using DotNetDevMCP.Orchestration;
using FluentAssertions;
using Xunit;

namespace DotNetDevMCP.Core.Tests;

/// <summary>
/// Tests for WorkflowEngine - ensures proper dependency management and parallel execution
/// </summary>
public class WorkflowEngineTests
{
    [Fact]
    public async Task ExecuteAsync_WithSimpleSequentialSteps_ExecutesInOrder()
    {
        // Arrange
        var engine = new WorkflowEngine();
        var executionOrder = new List<string>();

        var workflow = new TestWorkflow("SimpleFlow")
            .AddStep("Step1", async (ctx, ct) =>
            {
                executionOrder.Add("Step1");
                ctx.Set("Step1Result", "Done");
                await Task.Delay(10, ct);
                return new StepResult(true);
            })
            .AddStep("Step2", async (ctx, ct) =>
            {
                executionOrder.Add("Step2");
                ctx.Get<string>("Step1Result").Should().Be("Done");
                await Task.Delay(10, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Step1" })
            .AddStep("Step3", async (ctx, ct) =>
            {
                executionOrder.Add("Step3");
                await Task.Delay(10, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Step2" });

        // Act
        var result = await engine.ExecuteAsync(workflow, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TotalSteps.Should().Be(3);
        result.SuccessfulSteps.Should().Be(3);
        executionOrder.Should().ContainInOrder("Step1", "Step2", "Step3");
    }

    [Fact]
    public async Task ExecuteAsync_WithParallelSteps_ExecutesConcurrently()
    {
        // Arrange
        var engine = new WorkflowEngine();
        var startTimes = new Dictionary<string, DateTime>();
        var lockObj = new object();

        var workflow = new TestWorkflow("ParallelFlow")
            .AddStep("Setup", async (ctx, ct) =>
            {
                lock (lockObj) { startTimes["Setup"] = DateTime.UtcNow; }
                await Task.Delay(10, ct);
                return new StepResult(true);
            })
            .AddStep("Parallel1", async (ctx, ct) =>
            {
                lock (lockObj) { startTimes["Parallel1"] = DateTime.UtcNow; }
                await Task.Delay(100, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Setup" }, canParallel: true)
            .AddStep("Parallel2", async (ctx, ct) =>
            {
                lock (lockObj) { startTimes["Parallel2"] = DateTime.UtcNow; }
                await Task.Delay(100, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Setup" }, canParallel: true)
            .AddStep("Parallel3", async (ctx, ct) =>
            {
                lock (lockObj) { startTimes["Parallel3"] = DateTime.UtcNow; }
                await Task.Delay(100, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Setup" }, canParallel: true);

        // Act
        var result = await engine.ExecuteAsync(workflow, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.SuccessfulSteps.Should().Be(4);

        // Parallel steps should start around the same time
        var parallel1Start = startTimes["Parallel1"];
        var parallel2Start = startTimes["Parallel2"];
        var parallel3Start = startTimes["Parallel3"];

        var maxDiff = new[]
        {
            Math.Abs((parallel2Start - parallel1Start).TotalMilliseconds),
            Math.Abs((parallel3Start - parallel1Start).TotalMilliseconds),
            Math.Abs((parallel3Start - parallel2Start).TotalMilliseconds)
        }.Max();

        maxDiff.Should().BeLessThan(50, "parallel steps should start nearly simultaneously");
    }

    [Fact]
    public async Task ExecuteAsync_WithFailedStep_StopsExecution()
    {
        // Arrange
        var engine = new WorkflowEngine();
        var executedSteps = new List<string>();

        var workflow = new TestWorkflow("FailingFlow")
            .AddStep("Step1", async (ctx, ct) =>
            {
                executedSteps.Add("Step1");
                await Task.CompletedTask;
                return new StepResult(true);
            })
            .AddStep("FailingStep", async (ctx, ct) =>
            {
                executedSteps.Add("FailingStep");
                await Task.CompletedTask;
                return new StepResult(false, "Intentional failure");
            }, dependsOn: new[] { "Step1" })
            .AddStep("Step3", async (ctx, ct) =>
            {
                executedSteps.Add("Step3");
                await Task.CompletedTask;
                return new StepResult(true);
            }, dependsOn: new[] { "FailingStep" });

        // Act
        var result = await engine.ExecuteAsync(workflow, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.HasErrors.Should().BeTrue();
        result.FailedSteps.Should().Be(1);

        executedSteps.Should().Contain("Step1");
        executedSteps.Should().Contain("FailingStep");
        executedSteps.Should().NotContain("Step3", "dependent step should not execute after failure");
    }

    [Fact]
    public async Task ExecuteAsync_WithContextPassing_SharesDataBetweenSteps()
    {
        // Arrange
        var engine = new WorkflowEngine();

        var workflow = new TestWorkflow("ContextFlow")
            .AddStep("ProduceData", async (ctx, ct) =>
            {
                ctx.Set("UserId", 123);
                ctx.Set("UserName", "Alice");
                await Task.CompletedTask;
                return new StepResult(true);
            })
            .AddStep("ConsumeData", async (ctx, ct) =>
            {
                var userId = ctx.Get<int>("UserId");
                var userName = ctx.Get<string>("UserName");

                userId.Should().Be(123);
                userName.Should().Be("Alice");

                ctx.Set("ProcessedResult", $"User {userName} ({userId}) processed");
                await Task.CompletedTask;
                return new StepResult(true);
            }, dependsOn: new[] { "ProduceData" });

        // Act
        var result = await engine.ExecuteAsync(workflow, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.FinalContext.Get<string>("ProcessedResult").Should().Be("User Alice (123) processed");
    }

    [Fact]
    public async Task ExecuteAsync_WithProgress_ReportsProgress()
    {
        // Arrange
        var engine = new WorkflowEngine();
        var progressReports = new List<WorkflowProgress>();
        var progress = new Progress<WorkflowProgress>(p => progressReports.Add(p));

        var workflow = new TestWorkflow("ProgressFlow")
            .AddStep("Step1", async (ctx, ct) => { await Task.Delay(20, ct); return new StepResult(true); })
            .AddStep("Step2", async (ctx, ct) => { await Task.Delay(20, ct); return new StepResult(true); }, dependsOn: new[] { "Step1" })
            .AddStep("Step3", async (ctx, ct) => { await Task.Delay(20, ct); return new StepResult(true); }, dependsOn: new[] { "Step2" });

        // Act
        var result = await engine.ExecuteAsync(workflow, progress, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        progressReports.Should().NotBeEmpty();

        var maxCompleted = progressReports.Max(p => p.CompletedSteps);
        maxCompleted.Should().Be(3);

        progressReports.Should().Contain(p => p.PercentComplete == 100.0);
    }

    [Fact]
    public async Task ExecuteAsync_SupportsCancellation()
    {
        // Arrange
        var engine = new WorkflowEngine();
        using var cts = new CancellationTokenSource();
        var stepStarted = new TaskCompletionSource<bool>();

        var workflow = new TestWorkflow("CancellableFlow")
            .AddStep("LongStep", async (ctx, ct) =>
            {
                stepStarted.SetResult(true); // Signal that step has started
                await Task.Delay(10000, ct); // Very long delay to ensure cancellation
                return new StepResult(true);
            });

        // Act
        var task = engine.ExecuteAsync(workflow, cancellationToken: cts.Token);

        await stepStarted.Task; // Wait for step to start
        cts.Cancel(); // Cancel immediately

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task ExecuteAsync_WithComplexDependencyGraph_ExecutesCorrectly()
    {
        // Arrange
        var engine = new WorkflowEngine();
        var executionOrder = new List<string>();
        var lockObj = new object();

        /*
         * Dependency graph:
         *     Step1
         *     /   \
         *  Step2  Step3
         *     \   /
         *     Step4
         */

        var workflow = new TestWorkflow("ComplexGraph")
            .AddStep("Step1", async (ctx, ct) =>
            {
                lock (lockObj) { executionOrder.Add("Step1"); }
                await Task.Delay(10, ct);
                return new StepResult(true);
            })
            .AddStep("Step2", async (ctx, ct) =>
            {
                lock (lockObj) { executionOrder.Add("Step2"); }
                await Task.Delay(10, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Step1" }, canParallel: true)
            .AddStep("Step3", async (ctx, ct) =>
            {
                lock (lockObj) { executionOrder.Add("Step3"); }
                await Task.Delay(10, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Step1" }, canParallel: true)
            .AddStep("Step4", async (ctx, ct) =>
            {
                lock (lockObj) { executionOrder.Add("Step4"); }
                await Task.Delay(10, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Step2", "Step3" });

        // Act
        var result = await engine.ExecuteAsync(workflow, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.SuccessfulSteps.Should().Be(4);

        // Step1 should be first
        executionOrder.First().Should().Be("Step1");

        // Step2 and Step3 should be before Step4
        var step2Index = executionOrder.IndexOf("Step2");
        var step3Index = executionOrder.IndexOf("Step3");
        var step4Index = executionOrder.IndexOf("Step4");

        step2Index.Should().BeLessThan(step4Index);
        step3Index.Should().BeLessThan(step4Index);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyWorkflow_ReturnsSuccess()
    {
        // Arrange
        var engine = new WorkflowEngine();
        var workflow = new TestWorkflow("EmptyFlow");

        // Act
        var result = await engine.ExecuteAsync(workflow, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TotalSteps.Should().Be(0);
        result.StepResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_MeasuresDuration()
    {
        // Arrange
        var engine = new WorkflowEngine();

        var workflow = new TestWorkflow("DurationFlow")
            .AddStep("Step1", async (ctx, ct) => { await Task.Delay(50, ct); return new StepResult(true); })
            .AddStep("Step2", async (ctx, ct) => { await Task.Delay(50, ct); return new StepResult(true); }, dependsOn: new[] { "Step1" });

        // Act
        var result = await engine.ExecuteAsync(workflow, CancellationToken.None);

        // Assert
        result.Duration.Should().BeGreaterThan(TimeSpan.FromMilliseconds(80));
        result.Duration.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ExecuteAsync_WithException_CapturesError()
    {
        // Arrange
        var engine = new WorkflowEngine();

        var workflow = new TestWorkflow("ExceptionFlow")
            .AddStep("ThrowingStep", (ctx, ct) =>
            {
                throw new InvalidOperationException("Test exception");
            });

        // Act
        var result = await engine.ExecuteAsync(workflow, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailedSteps.Should().Be(1);

        var failedStep = result.StepResults.First();
        failedStep.IsSuccess.Should().BeFalse();
        failedStep.Error.Should().Contain("Test exception");
    }
}

/// <summary>
/// Test helper class for creating workflows
/// </summary>
internal class TestWorkflow : IWorkflow
{
    private readonly List<TestWorkflowStep> _steps = new();

    public TestWorkflow(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public IEnumerable<IWorkflowStep> Steps => _steps;

    public TestWorkflow AddStep(
        string name,
        Func<WorkflowContext, CancellationToken, Task<StepResult>> execute,
        string[]? dependsOn = null,
        bool canParallel = false)
    {
        _steps.Add(new TestWorkflowStep(name, execute, dependsOn ?? Array.Empty<string>(), canParallel));
        return this;
    }
}

internal class TestWorkflowStep : IWorkflowStep
{
    private readonly Func<WorkflowContext, CancellationToken, Task<StepResult>> _execute;

    public TestWorkflowStep(
        string name,
        Func<WorkflowContext, CancellationToken, Task<StepResult>> execute,
        string[] dependsOn,
        bool canParallel)
    {
        Name = name;
        _execute = execute;
        DependsOn = dependsOn;
        CanExecuteInParallel = canParallel;
    }

    public string Name { get; }
    public bool CanExecuteInParallel { get; }
    public IEnumerable<string> DependsOn { get; }

    public Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default)
    {
        return _execute(context, cancellationToken);
    }
}
