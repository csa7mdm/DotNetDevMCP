// Copyright (c) 2025 Ahmed Mustafa

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using DotNetDevMCP.Orchestration;

namespace DotNetDevMCP.Benchmarks;

/// <summary>
/// Benchmarks comparing sequential vs parallel execution with orchestration components
/// Target: 50-80% performance improvement for concurrent operations
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class OrchestrationBenchmarks
{
    private const int OperationCount = 20;
    private const int OperationDelayMs = 50;

    [Benchmark(Baseline = true, Description = "Sequential execution (baseline)")]
    public async Task<int> Sequential_Baseline()
    {
        var results = new List<int>();
        for (int i = 0; i < OperationCount; i++)
        {
            await Task.Delay(OperationDelayMs);
            results.Add(i);
        }
        return results.Count;
    }

    [Benchmark(Description = "Parallel with Task.WhenAll")]
    public async Task<int> Parallel_TaskWhenAll()
    {
        var tasks = Enumerable.Range(0, OperationCount)
            .Select(async i =>
            {
                await Task.Delay(OperationDelayMs);
                return i;
            });

        var results = await Task.WhenAll(tasks);
        return results.Length;
    }

    [Benchmark(Description = "Parallel with ConcurrentExecutor (no throttling)")]
    public async Task<int> Parallel_ConcurrentExecutor_NoThrottle()
    {
        var executor = new ConcurrentExecutor();
        var operations = Enumerable.Range(0, OperationCount)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                await Task.Delay(OperationDelayMs, ct);
                return i;
            });

        var result = await executor.ExecuteAsync(
            operations,
            new ConcurrentExecutionOptions(MaxDegreeOfParallelism: null),
            CancellationToken.None);

        return result.SuccessfulResults.Count();
    }

    [Benchmark(Description = "Parallel with ConcurrentExecutor (throttled to 5)")]
    public async Task<int> Parallel_ConcurrentExecutor_Throttled()
    {
        var executor = new ConcurrentExecutor();
        var operations = Enumerable.Range(0, OperationCount)
            .Select<int, Func<CancellationToken, Task<int>>>(i => async ct =>
            {
                await Task.Delay(OperationDelayMs, ct);
                return i;
            });

        var result = await executor.ExecuteAsync(
            operations,
            new ConcurrentExecutionOptions(MaxDegreeOfParallelism: 5),
            CancellationToken.None);

        return result.SuccessfulResults.Count();
    }

    [Benchmark(Description = "ResourceManager with throttling")]
    public async Task<int> ResourceManager_WithThrottling()
    {
        var resourceManager = new ResourceManager { MaxConcurrency = 5 };
        var tasks = Enumerable.Range(0, OperationCount)
            .Select(i => resourceManager.ExecuteWithThrottlingAsync(async () =>
            {
                await Task.Delay(OperationDelayMs);
                return i;
            }));

        var results = await Task.WhenAll(tasks);
        return results.Length;
    }

    [Benchmark(Description = "WorkflowEngine with dependencies")]
    public async Task<int> WorkflowEngine_WithDependencies()
    {
        var engine = new WorkflowEngine();
        var workflow = CreateBenchmarkWorkflow();

        var result = await engine.ExecuteAsync(workflow, CancellationToken.None);
        return result.StepResults.Count();
    }

    [Benchmark(Description = "OrchestrationService full stack")]
    public async Task<int> OrchestrationService_FullStack()
    {
        var service = new OrchestrationService();
        service.ResourceManager.MaxConcurrency = 5;

        // Register benchmark tool
        service.RegisterTool("benchmark", async (args, ct) =>
        {
            await Task.Delay(OperationDelayMs, ct);
            return ToolResult.Success($"Processed {args}");
        });

        var operations = Enumerable.Range(0, OperationCount)
            .Select(i => ("benchmark", i.ToString()));

        var results = await service.ExecuteParallelAsync(operations);
        return results.Count();
    }

    private static IWorkflow CreateBenchmarkWorkflow()
    {
        var workflow = new BenchmarkWorkflow("Performance Test");

        // Initial step
        workflow.AddStep("Init", async (ctx, ct) =>
        {
            await Task.Delay(OperationDelayMs, ct);
            return new StepResult(true);
        });

        // 5 parallel steps depending on Init
        for (int i = 1; i <= 5; i++)
        {
            var stepNum = i;
            workflow.AddStep($"Parallel{stepNum}", async (ctx, ct) =>
            {
                await Task.Delay(OperationDelayMs, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Init" }, canParallel: true);
        }

        // Final aggregation step
        workflow.AddStep("Finalize", async (ctx, ct) =>
        {
            await Task.Delay(OperationDelayMs, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Parallel1", "Parallel2", "Parallel3", "Parallel4", "Parallel5" });

        return workflow;
    }
}

/// <summary>
/// Benchmarks for batch operations comparing sequential vs concurrent
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class BatchOperationBenchmarks
{
    private const int BatchSize = 100;
    private const int OperationDelayMs = 10;

    [Benchmark(Baseline = true, Description = "Sequential batch processing")]
    public async Task<int> Sequential_Batch()
    {
        var count = 0;
        for (int i = 0; i < BatchSize; i++)
        {
            await Task.Delay(OperationDelayMs);
            count++;
        }
        return count;
    }

    [Benchmark(Description = "Concurrent batch with ResourceManager")]
    public async Task<int> Concurrent_ResourceManager_Batch()
    {
        var resourceManager = new ResourceManager { MaxConcurrency = 10 };
        var results = await resourceManager.ExecuteBatchWithThrottlingAsync(
            Enumerable.Range(0, BatchSize).Select<int, Func<Task<int>>>(i => async () =>
            {
                await Task.Delay(OperationDelayMs);
                return i;
            }));

        return results.Count();
    }
}

/// <summary>
/// Benchmarks for workflow scenarios
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class WorkflowBenchmarks
{
    private const int StepDelayMs = 20;

    [Benchmark(Baseline = true, Description = "Sequential workflow steps")]
    public async Task Sequential_Workflow()
    {
        // Simulate 10 sequential steps
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(StepDelayMs);
        }
    }

    [Benchmark(Description = "Workflow with parallel opportunities")]
    public async Task Parallel_Workflow_WithEngine()
    {
        var engine = new WorkflowEngine();
        var workflow = CreateComplexWorkflow();
        await engine.ExecuteAsync(workflow, CancellationToken.None);
    }

    private static IWorkflow CreateComplexWorkflow()
    {
        var workflow = new BenchmarkWorkflow("Complex Workflow");

        // Step 1: Initial
        workflow.AddStep("Step1", async (ctx, ct) =>
        {
            await Task.Delay(StepDelayMs, ct);
            return new StepResult(true);
        });

        // Steps 2-4: Parallel after Step1
        for (int i = 2; i <= 4; i++)
        {
            var stepNum = i;
            workflow.AddStep($"Step{stepNum}", async (ctx, ct) =>
            {
                await Task.Delay(StepDelayMs, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Step1" }, canParallel: true);
        }

        // Step 5: After 2-4
        workflow.AddStep("Step5", async (ctx, ct) =>
        {
            await Task.Delay(StepDelayMs, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Step2", "Step3", "Step4" });

        // Steps 6-8: Parallel after Step5
        for (int i = 6; i <= 8; i++)
        {
            var stepNum = i;
            workflow.AddStep($"Step{stepNum}", async (ctx, ct) =>
            {
                await Task.Delay(StepDelayMs, ct);
                return new StepResult(true);
            }, dependsOn: new[] { "Step5" }, canParallel: true);
        }

        // Steps 9-10: Sequential after 6-8
        workflow.AddStep("Step9", async (ctx, ct) =>
        {
            await Task.Delay(StepDelayMs, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Step6", "Step7", "Step8" });

        workflow.AddStep("Step10", async (ctx, ct) =>
        {
            await Task.Delay(StepDelayMs, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Step9" });

        return workflow;
    }
}

internal class BenchmarkWorkflow : IWorkflow
{
    private readonly List<BenchmarkWorkflowStep> _steps = new();

    public BenchmarkWorkflow(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public IEnumerable<IWorkflowStep> Steps => _steps;

    public BenchmarkWorkflow AddStep(
        string name,
        Func<WorkflowContext, CancellationToken, Task<StepResult>> execute,
        string[]? dependsOn = null,
        bool canParallel = false)
    {
        _steps.Add(new BenchmarkWorkflowStep(name, execute, dependsOn ?? Array.Empty<string>(), canParallel));
        return this;
    }
}

internal class BenchmarkWorkflowStep : IWorkflowStep
{
    private readonly Func<WorkflowContext, CancellationToken, Task<StepResult>> _execute;

    public BenchmarkWorkflowStep(
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
