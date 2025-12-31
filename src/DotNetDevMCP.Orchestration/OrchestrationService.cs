// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using System.Collections.Concurrent;

namespace DotNetDevMCP.Orchestration;

/// <summary>
/// Main orchestration service that coordinates ResourceManager, ConcurrentExecutor, and WorkflowEngine
/// </summary>
public class OrchestrationService : IOrchestrationService
{
    private readonly ConcurrentDictionary<string, Func<string, CancellationToken, Task<ToolResult>>> _tools = new();
    private readonly IResourceManager _resourceManager;
    private readonly IConcurrentExecutor _concurrentExecutor;
    private readonly IWorkflowEngine _workflowEngine;

    public OrchestrationService()
        : this(new ResourceManager(), new ConcurrentExecutor(), new WorkflowEngine())
    {
    }

    public OrchestrationService(
        IResourceManager resourceManager,
        IConcurrentExecutor concurrentExecutor,
        IWorkflowEngine workflowEngine)
    {
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _concurrentExecutor = concurrentExecutor ?? throw new ArgumentNullException(nameof(concurrentExecutor));
        _workflowEngine = workflowEngine ?? throw new ArgumentNullException(nameof(workflowEngine));
    }

    /// <inheritdoc />
    public IResourceManager ResourceManager => _resourceManager;

    /// <inheritdoc />
    public async Task<IEnumerable<ToolResult>> ExecuteParallelAsync(
        IEnumerable<(string toolName, string arguments)> operations,
        CancellationToken cancellationToken = default)
    {
        var operationsList = operations.ToList();

        if (!operationsList.Any())
        {
            return Array.Empty<ToolResult>();
        }

        // Create operations that execute tools with resource management
        var toolOperations = operationsList.Select<(string toolName, string arguments), Func<CancellationToken, Task<ToolResult>>>(
            op => async ct =>
            {
                var (toolName, arguments) = op;

                // Resolve tool
                if (!_tools.TryGetValue(toolName, out var toolFunc))
                {
                    return ToolResult.Failure($"Tool '{toolName}' not found");
                }

                // Execute with resource management
                return await _resourceManager.ExecuteWithThrottlingAsync(
                    async () => await toolFunc(arguments, ct),
                    ct);
            });

        // Execute all operations concurrently
        var result = await _concurrentExecutor.ExecuteAsync(
            toolOperations,
            new ConcurrentExecutionOptions(ContinueOnError: true),
            cancellationToken);

        return result.SuccessfulResults;
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteWorkflowAsync(
        IWorkflow workflow,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _workflowEngine.ExecuteAsync(workflow, cancellationToken);

            if (result.IsSuccess)
            {
                return ToolResult.Success(
                    $"Workflow '{workflow.Name}' completed successfully. " +
                    $"Executed {result.SuccessfulSteps}/{result.TotalSteps} steps in {result.Duration.TotalSeconds:F2}s");
            }
            else
            {
                var failedSteps = result.StepResults.Where(r => !r.IsSuccess).ToList();
                var errorMessage = $"Workflow '{workflow.Name}' failed. " +
                    $"Completed {result.SuccessfulSteps}/{result.TotalSteps} steps. " +
                    $"Failed steps: {string.Join(", ", failedSteps.Select(s => s.StepName))}";

                return ToolResult.Failure(errorMessage);
            }
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Workflow '{workflow.Name}' failed with exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Registers a tool for execution (used for testing and dynamic tool registration)
    /// </summary>
    public void RegisterTool(string toolName, Func<string, CancellationToken, Task<ToolResult>> toolFunc)
    {
        _tools[toolName] = toolFunc ?? throw new ArgumentNullException(nameof(toolFunc));
    }

    /// <summary>
    /// Unregisters a tool
    /// </summary>
    public bool UnregisterTool(string toolName)
    {
        return _tools.TryRemove(toolName, out _);
    }

    /// <summary>
    /// Gets all registered tool names
    /// </summary>
    public IEnumerable<string> GetRegisteredTools()
    {
        return _tools.Keys.ToList();
    }
}
