// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using DotNetDevMCP.Orchestration;
using System.Diagnostics;

namespace DotNetDevMCP.Samples.OrchestrationDemo;

/// <summary>
/// Comprehensive integration demo showing ResourceManager, ConcurrentExecutor,
/// WorkflowEngine, and OrchestrationService working together
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("DotNetDevMCP Orchestration Demo");
        Console.WriteLine("Demonstrating concurrent operations and workflow orchestration");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        await RunDemo1_ResourceManagerThrottling();
        await RunDemo2_ParallelExecutor();
        await RunDemo3_WorkflowEngine();
        await RunDemo4_OrchestrationService();
        await RunDemo5_CompleteDevWorkflow();

        Console.WriteLine();
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("Demo completed successfully!");
        Console.WriteLine("=".PadRight(80, '='));
    }

    /// <summary>
    /// Demo 1: ResourceManager throttling to prevent resource exhaustion
    /// </summary>
    private static async Task RunDemo1_ResourceManagerThrottling()
    {
        Console.WriteLine("DEMO 1: ResourceManager Throttling");
        Console.WriteLine("-".PadRight(80, '-'));

        var resourceManager = new ResourceManager { MaxConcurrency = 3 };
        var random = new Random(42); // Seeded for reproducible results

        Console.WriteLine($"Max Concurrency: {resourceManager.MaxConcurrency}");
        Console.WriteLine("Executing 10 operations that would normally overwhelm resources...");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var operations = Enumerable.Range(1, 10)
            .Select(i => resourceManager.ExecuteWithThrottlingAsync(async () =>
            {
                var delay = random.Next(100, 300);
                Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] Operation {i} started (will take {delay}ms)");
                await Task.Delay(delay);
                Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] Operation {i} completed");
                return i;
            }))
            .ToList();

        var results = await Task.WhenAll(operations);
        sw.Stop();

        var metrics = resourceManager.GetMetrics();
        Console.WriteLine();
        Console.WriteLine($"Total Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Metrics - Executed: {metrics.TotalExecuted}, Failed: {metrics.TotalFailed}");
        Console.WriteLine($"Average Execution Time: {metrics.AverageExecutionTime.TotalMilliseconds:F2}ms");
        Console.WriteLine();
    }

    /// <summary>
    /// Demo 2: ConcurrentExecutor for parallel operations with error handling
    /// </summary>
    private static async Task RunDemo2_ParallelExecutor()
    {
        Console.WriteLine("DEMO 2: ConcurrentExecutor with Error Handling");
        Console.WriteLine("-".PadRight(80, '-'));

        var executor = new ConcurrentExecutor();
        var random = new Random(42);

        // Create operations that simulate file processing (some fail)
        var operations = new List<Func<CancellationToken, Task<string>>>();
        for (int i = 1; i <= 8; i++)
        {
            var index = i;
            operations.Add(async ct =>
            {
                var delay = random.Next(50, 150);
                await Task.Delay(delay, ct);

                // Simulate 25% failure rate
                if (index % 4 == 0)
                {
                    throw new InvalidOperationException($"File {index} is corrupted");
                }

                return $"File_{index}.cs";
            });
        }

        Console.WriteLine("Processing 8 files in parallel (some will fail)...");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(
            operations,
            new ConcurrentExecutionOptions(
                MaxDegreeOfParallelism: 4,
                ContinueOnError: true,
                OperationTimeout: TimeSpan.FromSeconds(5)));
        sw.Stop();

        Console.WriteLine($"Total Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Successful: {result.SuccessfulOperations}/{result.TotalOperations}");
        Console.WriteLine($"Errors: {result.Errors.Count()}");
        Console.WriteLine();
        Console.WriteLine("Successful files:");
        foreach (var file in result.SuccessfulResults)
        {
            Console.WriteLine($"  ✓ {file}");
        }
        Console.WriteLine();
        Console.WriteLine("Failed files:");
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"  ✗ Operation {error.OperationIndex}: {error.Message}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Demo 3: WorkflowEngine with dependencies and parallel steps
    /// </summary>
    private static async Task RunDemo3_WorkflowEngine()
    {
        Console.WriteLine("DEMO 3: WorkflowEngine - Build Pipeline");
        Console.WriteLine("-".PadRight(80, '-'));

        var engine = new WorkflowEngine();
        var workflow = CreateBuildPipelineWorkflow();

        Console.WriteLine($"Workflow: {workflow.Name}");
        Console.WriteLine($"Total Steps: {workflow.Steps.Count()}");
        Console.WriteLine();

        var progress = new Progress<WorkflowProgress>(p =>
        {
            if (p.CurrentStepName != null)
            {
                Console.WriteLine($"  [{p.CompletedSteps}/{p.TotalSteps}] Starting: {p.CurrentStepName}");
            }
        });

        var sw = Stopwatch.StartNew();
        var result = await engine.ExecuteAsync(workflow, progress, CancellationToken.None);
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine($"Workflow Status: {(result.IsSuccess ? "SUCCESS" : "FAILED")}");
        Console.WriteLine($"Total Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Steps Completed: {result.SuccessfulSteps}/{result.TotalSteps}");
        Console.WriteLine();

        Console.WriteLine("Step Details:");
        foreach (var step in result.StepResults)
        {
            var status = step.IsSuccess ? "✓" : "✗";
            Console.WriteLine($"  {status} {step.StepName} ({step.Duration.TotalMilliseconds}ms)");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Demo 4: OrchestrationService coordinating tools
    /// </summary>
    private static async Task RunDemo4_OrchestrationService()
    {
        Console.WriteLine("DEMO 4: OrchestrationService - Tool Coordination");
        Console.WriteLine("-".PadRight(80, '-'));

        var service = new OrchestrationService();
        service.ResourceManager.MaxConcurrency = 3;

        // Register mock tools
        service.RegisterTool("compile", async (args, ct) =>
        {
            Console.WriteLine($"  Compiling {args}...");
            await Task.Delay(100, ct);
            return ToolResult.Success($"Compiled {args}");
        });

        service.RegisterTool("test", async (args, ct) =>
        {
            Console.WriteLine($"  Testing {args}...");
            await Task.Delay(150, ct);
            return ToolResult.Success($"Tests passed for {args}");
        });

        service.RegisterTool("analyze", async (args, ct) =>
        {
            Console.WriteLine($"  Analyzing {args}...");
            await Task.Delay(80, ct);
            return ToolResult.Success($"Analysis complete for {args}");
        });

        var operations = new[]
        {
            ("compile", "Module.Core"),
            ("compile", "Module.Services"),
            ("compile", "Module.API"),
            ("test", "Module.Core.Tests"),
            ("test", "Module.Services.Tests"),
            ("analyze", "Solution")
        };

        Console.WriteLine("Executing tools in parallel with throttling...");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var results = await service.ExecuteParallelAsync(operations);
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine($"Total Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Results: {results.Count(r => r.IsSuccess)}/{results.Count()} successful");
        Console.WriteLine();
    }

    /// <summary>
    /// Demo 5: Complete development workflow combining all components
    /// </summary>
    private static async Task RunDemo5_CompleteDevWorkflow()
    {
        Console.WriteLine("DEMO 5: Complete Development Workflow");
        Console.WriteLine("-".PadRight(80, '-'));

        var service = new OrchestrationService();
        service.ResourceManager.MaxConcurrency = 4;

        // Register development tools
        RegisterDevelopmentTools(service);

        var workflow = CreateCompleteDevWorkflow();

        Console.WriteLine($"Workflow: {workflow.Name}");
        Console.WriteLine("This demonstrates a complete CI/CD pipeline with:");
        Console.WriteLine("  - Source code analysis");
        Console.WriteLine("  - Parallel compilation");
        Console.WriteLine("  - Parallel testing");
        Console.WriteLine("  - Code quality checks");
        Console.WriteLine("  - Package and deploy");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var result = await service.ExecuteWorkflowAsync(workflow);
        sw.Stop();

        Console.WriteLine($"Workflow completed in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Status: {(result.IsSuccess ? "SUCCESS ✓" : "FAILED ✗")}");
        Console.WriteLine();
    }

    private static IWorkflow CreateBuildPipelineWorkflow()
    {
        var workflow = new DemoWorkflow("Build Pipeline");

        workflow.AddStep("Restore", async (ctx, ct) =>
        {
            await Task.Delay(200, ct);
            ctx.Set("PackagesRestored", 42);
            return new StepResult(true);
        });

        workflow.AddStep("Build_Core", async (ctx, ct) =>
        {
            await Task.Delay(150, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Restore" }, canParallel: true);

        workflow.AddStep("Build_Services", async (ctx, ct) =>
        {
            await Task.Delay(180, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Restore" }, canParallel: true);

        workflow.AddStep("Build_API", async (ctx, ct) =>
        {
            await Task.Delay(160, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Restore" }, canParallel: true);

        workflow.AddStep("Test", async (ctx, ct) =>
        {
            await Task.Delay(250, ct);
            var packages = ctx.Get<int>("PackagesRestored");
            ctx.Set("TestsPassed", true);
            return new StepResult(true);
        }, dependsOn: new[] { "Build_Core", "Build_Services", "Build_API" });

        workflow.AddStep("Package", async (ctx, ct) =>
        {
            await Task.Delay(100, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Test" });

        return workflow;
    }

    private static void RegisterDevelopmentTools(OrchestrationService service)
    {
        service.RegisterTool("git-clone", async (args, ct) =>
        {
            await Task.Delay(100, ct);
            return ToolResult.Success("Repository cloned");
        });

        service.RegisterTool("restore", async (args, ct) =>
        {
            await Task.Delay(150, ct);
            return ToolResult.Success("Packages restored");
        });

        service.RegisterTool("build", async (args, ct) =>
        {
            await Task.Delay(120, ct);
            return ToolResult.Success($"Built {args}");
        });

        service.RegisterTool("test", async (args, ct) =>
        {
            await Task.Delay(180, ct);
            return ToolResult.Success($"Tests passed: {args}");
        });

        service.RegisterTool("analyze", async (args, ct) =>
        {
            await Task.Delay(90, ct);
            return ToolResult.Success("Code analysis complete");
        });

        service.RegisterTool("package", async (args, ct) =>
        {
            await Task.Delay(80, ct);
            return ToolResult.Success("Package created");
        });

        service.RegisterTool("deploy", async (args, ct) =>
        {
            await Task.Delay(100, ct);
            return ToolResult.Success($"Deployed to {args}");
        });
    }

    private static IWorkflow CreateCompleteDevWorkflow()
    {
        var workflow = new DemoWorkflow("Complete CI/CD Pipeline");

        workflow.AddStep("SourceControl", async (ctx, ct) =>
        {
            await Task.Delay(100, ct);
            ctx.Set("CommitHash", "a1b2c3d");
            return new StepResult(true);
        });

        workflow.AddStep("Restore", async (ctx, ct) =>
        {
            await Task.Delay(150, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "SourceControl" });

        workflow.AddStep("Build_Module1", async (ctx, ct) =>
        {
            await Task.Delay(120, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Restore" }, canParallel: true);

        workflow.AddStep("Build_Module2", async (ctx, ct) =>
        {
            await Task.Delay(130, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Restore" }, canParallel: true);

        workflow.AddStep("Build_Module3", async (ctx, ct) =>
        {
            await Task.Delay(110, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Restore" }, canParallel: true);

        workflow.AddStep("Test_Unit", async (ctx, ct) =>
        {
            await Task.Delay(180, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Build_Module1", "Build_Module2", "Build_Module3" }, canParallel: true);

        workflow.AddStep("Test_Integration", async (ctx, ct) =>
        {
            await Task.Delay(200, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "Build_Module1", "Build_Module2", "Build_Module3" }, canParallel: true);

        workflow.AddStep("CodeQuality", async (ctx, ct) =>
        {
            await Task.Delay(90, ct);
            ctx.Set("QualityScore", 95.5);
            return new StepResult(true);
        }, dependsOn: new[] { "Test_Unit", "Test_Integration" });

        workflow.AddStep("Package", async (ctx, ct) =>
        {
            await Task.Delay(80, ct);
            return new StepResult(true);
        }, dependsOn: new[] { "CodeQuality" });

        workflow.AddStep("Deploy", async (ctx, ct) =>
        {
            await Task.Delay(100, ct);
            var commitHash = ctx.Get<string>("CommitHash");
            var qualityScore = ctx.Get<double>("QualityScore");
            ctx.Set("DeploymentUrl", $"https://app.example.com/{commitHash}");
            return new StepResult(true);
        }, dependsOn: new[] { "Package" });

        return workflow;
    }
}

/// <summary>
/// Demo workflow implementation
/// </summary>
internal class DemoWorkflow : IWorkflow
{
    private readonly List<DemoWorkflowStep> _steps = new();

    public DemoWorkflow(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public IEnumerable<IWorkflowStep> Steps => _steps;

    public DemoWorkflow AddStep(
        string name,
        Func<WorkflowContext, CancellationToken, Task<StepResult>> execute,
        string[]? dependsOn = null,
        bool canParallel = false)
    {
        _steps.Add(new DemoWorkflowStep(name, execute, dependsOn ?? Array.Empty<string>(), canParallel));
        return this;
    }
}

internal class DemoWorkflowStep : IWorkflowStep
{
    private readonly Func<WorkflowContext, CancellationToken, Task<StepResult>> _execute;

    public DemoWorkflowStep(
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
