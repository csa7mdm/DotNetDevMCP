// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using System.Diagnostics;

namespace DotNetDevMCP.Orchestration;

/// <summary>
/// Executes workflows with dependency management and parallel step execution
/// </summary>
public class WorkflowEngine : IWorkflowEngine
{
    /// <inheritdoc />
    public async Task<WorkflowExecutionResult> ExecuteAsync(
        IWorkflow workflow,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(workflow, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkflowExecutionResult> ExecuteAsync(
        IWorkflow workflow,
        IProgress<WorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var context = new WorkflowContext();
        var stepResults = new List<StepExecutionResult>();
        var steps = workflow.Steps.ToList();

        if (!steps.Any())
        {
            stopwatch.Stop();
            return new WorkflowExecutionResult(
                IsSuccess: true,
                StepResults: Array.Empty<StepExecutionResult>(),
                FinalContext: context,
                Duration: stopwatch.Elapsed);
        }

        try
        {
            // Build dependency graph
            var dependencyGraph = BuildDependencyGraph(steps);

            // Execute steps in topological order
            var executedSteps = new HashSet<string>();
            var completedSteps = 0;

            while (executedSteps.Count < steps.Count)
            {
                // Find steps ready to execute (all dependencies met)
                var readySteps = steps
                    .Where(s => !executedSteps.Contains(s.Name))
                    .Where(s => s.DependsOn.All(dep => executedSteps.Contains(dep)))
                    .ToList();

                if (!readySteps.Any())
                {
                    // No steps ready - might have circular dependency or missing steps
                    throw new InvalidOperationException(
                        "No steps ready to execute. Check for circular dependencies or missing step references.");
                }

                // Group steps that can execute in parallel
                var parallelSteps = readySteps.Where(s => s.CanExecuteInParallel && readySteps.Count > 1).ToList();
                var sequentialSteps = readySteps.Except(parallelSteps).ToList();

                // Execute sequential steps first
                foreach (var step in sequentialSteps)
                {
                    ReportProgress(progress, steps.Count, completedSteps, step.Name);

                    var stepResult = await ExecuteStepAsync(step, context, cancellationToken);
                    stepResults.Add(stepResult);
                    executedSteps.Add(step.Name);
                    completedSteps++;

                    if (!stepResult.IsSuccess)
                    {
                        // Step failed - stop workflow
                        stopwatch.Stop();
                        return new WorkflowExecutionResult(
                            IsSuccess: false,
                            StepResults: stepResults,
                            FinalContext: context,
                            Duration: stopwatch.Elapsed);
                    }

                    ReportProgress(progress, steps.Count, completedSteps, null);
                }

                // Execute parallel steps
                if (parallelSteps.Any())
                {
                    var parallelTasks = parallelSteps.Select(step => Task.Run(async () =>
                    {
                        ReportProgress(progress, steps.Count, completedSteps, step.Name);
                        var result = await ExecuteStepAsync(step, context, cancellationToken);
                        return (step.Name, result);
                    }, cancellationToken));

                    var parallelResults = await Task.WhenAll(parallelTasks);

                    foreach (var (stepName, stepResult) in parallelResults)
                    {
                        stepResults.Add(stepResult);
                        executedSteps.Add(stepName);
                        Interlocked.Increment(ref completedSteps);

                        if (!stepResult.IsSuccess)
                        {
                            // Step failed - stop workflow
                            stopwatch.Stop();
                            return new WorkflowExecutionResult(
                                IsSuccess: false,
                                StepResults: stepResults,
                                FinalContext: context,
                                Duration: stopwatch.Elapsed);
                        }
                    }

                    ReportProgress(progress, steps.Count, completedSteps, null);
                }
            }

            stopwatch.Stop();

            // All steps completed successfully
            ReportProgress(progress, steps.Count, completedSteps, null);

            return new WorkflowExecutionResult(
                IsSuccess: true,
                StepResults: stepResults,
                FinalContext: context,
                Duration: stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (Exception)
        {
            stopwatch.Stop();

            // Unexpected error - return what we have
            return new WorkflowExecutionResult(
                IsSuccess: false,
                StepResults: stepResults,
                FinalContext: context,
                Duration: stopwatch.Elapsed);
        }
    }

    private async Task<StepExecutionResult> ExecuteStepAsync(
        IWorkflowStep step,
        WorkflowContext context,
        CancellationToken cancellationToken)
    {
        var stepwatch = Stopwatch.StartNew();

        try
        {
            var result = await step.ExecuteAsync(context, cancellationToken);
            stepwatch.Stop();

            return new StepExecutionResult(
                StepName: step.Name,
                IsSuccess: result.IsSuccess,
                Error: result.ErrorMessage,
                Duration: stepwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            // Allow cancellation to propagate
            stepwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stepwatch.Stop();

            return new StepExecutionResult(
                StepName: step.Name,
                IsSuccess: false,
                Error: ex.Message,
                Duration: stepwatch.Elapsed);
        }
    }

    private Dictionary<string, List<string>> BuildDependencyGraph(List<IWorkflowStep> steps)
    {
        var graph = new Dictionary<string, List<string>>();

        foreach (var step in steps)
        {
            graph[step.Name] = step.DependsOn.ToList();
        }

        // Validate that all dependencies exist
        foreach (var step in steps)
        {
            foreach (var dep in step.DependsOn)
            {
                if (!graph.ContainsKey(dep))
                {
                    throw new InvalidOperationException(
                        $"Step '{step.Name}' depends on '{dep}' which does not exist in the workflow.");
                }
            }
        }

        return graph;
    }

    private static void ReportProgress(
        IProgress<WorkflowProgress>? progress,
        int totalSteps,
        int completedSteps,
        string? currentStepName)
    {
        progress?.Report(new WorkflowProgress(
            TotalSteps: totalSteps,
            CompletedSteps: completedSteps,
            CurrentStepName: currentStepName));
    }
}
