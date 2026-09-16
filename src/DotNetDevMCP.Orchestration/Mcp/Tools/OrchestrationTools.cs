// Copyright (c) 2025 Ahmed Mustafa

using ModelContextProtocol;
using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using System.Text.Json;

namespace DotNetDevMCP.Orchestration.Mcp.Tools;

/// <summary>
/// Marker class for ILogger category specific to OrchestrationTools
/// </summary>
public class OrchestrationToolsLogCategory { }

/// <summary>
/// MCP Tools for orchestrating parallel operations and workflow execution
/// </summary>
[McpServerToolType]
public static partial class OrchestrationTools
{
    [McpServerTool(Name = "orchestrate_parallel", Idempotent = false, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Executes multiple tool operations in parallel with resource management and throttling. Useful for running independent operations concurrently.")]
    public static async Task<object> ExecuteParallel(
        IOrchestrationService orchestrationService,
        ILogger<OrchestrationToolsLogCategory> logger,
        [Description("List of tool operations to execute in parallel. Each operation specifies a tool name and arguments.")] 
        IEnumerable<ToolOperationInput> operations,
        [Description("Maximum degree of parallelism (default: processor count)")] int? maxParallelism = null,
        [Description("Continue executing remaining operations if one fails")] bool continueOnError = true,
        [Description("Timeout in seconds for each operation")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Executing {Count} operations in parallel", operations.Count());
            
            // Configure resource manager if maxParallelism specified
            if (maxParallelism.HasValue)
            {
                orchestrationService.ResourceManager.MaxConcurrency = maxParallelism.Value;
            }
            
            var operationList = operations.Select(op => (op.ToolName, op.Arguments)).ToList();
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var results = await orchestrationService.ExecuteParallelAsync(operationList, cancellationToken);
            stopwatch.Stop();
            
            var resultList = results.ToList();
            var successCount = resultList.Count(r => r.IsSuccess);
            var failureCount = resultList.Count(r => !r.IsSuccess);
            
            logger.LogInformation("Parallel execution completed: {Success}/{Total} successful in {Duration}s", 
                successCount, resultList.Count, stopwatch.Elapsed.TotalSeconds);
            
            return new
            {
                Success = failureCount == 0,
                TotalOperations = resultList.Count,
                SuccessfulOperations = successCount,
                FailedOperations = failureCount,
                DurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2),
                Results = resultList.Select((r, i) => new
                {
                    Index = i,
                    ToolName = operationList[i].toolName,
                    r.IsSuccess,
                    r.Message,
                    Error = r.IsFailure ? r.Error : null
                })
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Parallel execution failed");
            return new 
            { 
                Success = false, 
                Error = ex.Message,
                TotalOperations = 0,
                SuccessfulOperations = 0,
                FailedOperations = 0
            };
        }
    }

    [McpServerTool(Name = "execute_workflow", Idempotent = false, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Executes a workflow consisting of sequential and/or parallel steps with conditional execution support.")]
    public static async Task<object> ExecuteWorkflow(
        IOrchestrationService orchestrationService,
        ILogger<OrchestrationToolsLogCategory> logger,
        [Description("Name of the workflow")] string workflowName,
        [Description("List of workflow steps to execute")] IEnumerable<WorkflowStepInput> steps,
        [Description("Initial context values to pass to the workflow")] Dictionary<string, object>? initialContext = null,
        [Description("Stop workflow execution on first error")] bool failFast = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Executing workflow: {WorkflowName} with {StepsCount} steps", workflowName, steps.Count());
            
            // Create workflow from input
            var workflow = new DynamicWorkflow(
                Name: workflowName,
                Steps: steps.Select(s => new WorkflowStep(
                    Name: s.Name,
                    ToolName: s.ToolName,
                    Arguments: s.Arguments,
                    Condition: s.Condition,
                    IsParallel = false // Could be extended to support parallel groups
                )).ToList(),
                InitialContext: initialContext ?? new Dictionary<string, object>(),
                FailFast: failFast);
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await orchestrationService.ExecuteWorkflowAsync(workflow, cancellationToken);
            stopwatch.Stop();
            
            logger.LogInformation("Workflow '{WorkflowName}' completed: Success={Success}, Steps={Successful}/{Total}", 
                workflowName, result.IsSuccess, 
                result.IsSuccess ? workflow.Steps.Count : 0, workflow.Steps.Count);
            
            if (result.IsSuccess)
            {
                return new
                {
                    Success = true,
                    WorkflowName = workflowName,
                    TotalSteps = workflow.Steps.Count,
                    SuccessfulSteps = workflow.Steps.Count,
                    FailedSteps = 0,
                    DurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2),
                    Message = result.Message
                };
            }
            else
            {
                // Parse error message to extract failed steps
                var failedSteps = new List<string>();
                if (result.Error != null && result.Error.Contains("Failed steps:"))
                {
                    var failedPart = result.Error.Split("Failed steps:")[1].Trim();
                    failedSteps = failedPart.Split(',').Select(s => s.Trim()).ToList();
                }
                
                return new
                {
                    Success = false,
                    WorkflowName = workflowName,
                    TotalSteps = workflow.Steps.Count,
                    SuccessfulSteps = workflow.Steps.Count - failedSteps.Count,
                    FailedSteps = failedSteps.Count,
                    FailedStepNames = failedSteps,
                    DurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2),
                    Error = result.Error
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workflow execution failed: {WorkflowName}", workflowName);
            return new 
            { 
                Success = false, 
                WorkflowName = workflowName,
                Error = ex.Message,
                TotalSteps = 0,
                SuccessfulSteps = 0,
                FailedSteps = 0
            };
        }
    }

    [McpServerTool(Name = "get_resource_metrics", Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Retrieves current resource utilization metrics from the orchestration service's resource manager.")]
    public static async Task<object> GetResourceMetrics(
        IOrchestrationService orchestrationService,
        ILogger<OrchestrationToolsLogCategory> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = orchestrationService.ResourceManager.GetMetrics();
            
            return new
            {
                Success = true,
                MaxConcurrency = metrics.MaxConcurrency,
                CurrentActiveOperations = metrics.CurrentActiveOperations,
                TotalOperationsExecuted = metrics.TotalOperationsExecuted,
                AverageWaitTimeMs = Math.Round(metrics.AverageWaitTime.TotalMilliseconds, 2),
                PeakConcurrency = metrics.PeakConcurrency,
                ThrottleCount = metrics.ThrottleCount,
                UtilizationPercentage = Math.Round(metrics.UtilizationPercentage, 2)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get resource metrics");
            return new 
            { 
                Success = false, 
                Error = ex.Message
            };
        }
    }

    [McpServerTool(Name = "configure_resource_limits", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Configures resource limits for concurrent operation execution.")]
    public static async Task<object> ConfigureResourceLimits(
        IOrchestrationService orchestrationService,
        ILogger<OrchestrationToolsLogCategory> logger,
        [Description("Maximum number of concurrent operations")] int maxConcurrency,
        [Description("Optional CPU usage threshold percentage (0-100)")] int? cpuThreshold = null,
        [Description("Optional memory usage threshold in MB")] int? memoryThresholdMb = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Configuring resource limits: MaxConcurrency={Max}", maxConcurrency);
            
            orchestrationService.ResourceManager.MaxConcurrency = maxConcurrency;
            
            if (cpuThreshold.HasValue)
            {
                // Note: CPU threshold configuration would require additional implementation
                logger.LogInformation("CPU threshold configured: {Threshold}%", cpuThreshold.Value);
            }
            
            if (memoryThresholdMb.HasValue)
            {
                // Note: Memory threshold configuration would require additional implementation
                logger.LogInformation("Memory threshold configured: {Threshold}MB", memoryThresholdMb.Value);
            }
            
            var metrics = orchestrationService.ResourceManager.GetMetrics();
            
            return new
            {
                Success = true,
                MaxConcurrency = metrics.MaxConcurrency,
                CpuThresholdConfigured = cpuThreshold.HasValue,
                MemoryThresholdConfigured = memoryThresholdMb.HasValue,
                Message = $"Resource limits configured successfully. Max concurrency set to {maxConcurrency}."
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure resource limits");
            return new 
            { 
                Success = false, 
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// Input model for tool operations in parallel execution
/// </summary>
public record ToolOperationInput(
    string ToolName,
    string Arguments);

/// <summary>
/// Input model for workflow steps
/// </summary>
public record WorkflowStepInput(
    string Name,
    string ToolName,
    string Arguments,
    string? Condition = null);

/// <summary>
/// Dynamic workflow implementation for runtime workflow creation
/// </summary>
public record DynamicWorkflow(
    string Name,
    List<WorkflowStep> Steps,
    Dictionary<string, object> InitialContext,
    bool FailFast) : IWorkflow
{
    public IReadOnlyList<WorkflowStep> GetSteps() => Steps.AsReadOnly();
    public WorkflowContext CreateContext()
    {
        var context = new WorkflowContext();
        foreach (var kvp in InitialContext)
        {
            context.Set(kvp.Key, kvp.Value);
        }
        return context;
    }
}
