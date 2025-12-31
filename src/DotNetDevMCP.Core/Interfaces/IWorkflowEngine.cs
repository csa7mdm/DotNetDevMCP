// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;

namespace DotNetDevMCP.Core.Interfaces;

/// <summary>
/// Executes workflows with dependency management and parallel step execution
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Executes a workflow
    /// </summary>
    Task<WorkflowExecutionResult> ExecuteAsync(
        IWorkflow workflow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a workflow with progress reporting
    /// </summary>
    Task<WorkflowExecutionResult> ExecuteAsync(
        IWorkflow workflow,
        IProgress<WorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a workflow with steps
/// </summary>
public interface IWorkflow
{
    /// <summary>
    /// Gets the workflow name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the workflow steps
    /// </summary>
    IEnumerable<IWorkflowStep> Steps { get; }
}

/// <summary>
/// Represents a step in a workflow
/// </summary>
public interface IWorkflowStep
{
    /// <summary>
    /// Gets the step name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets whether this step can execute in parallel with others
    /// </summary>
    bool CanExecuteInParallel { get; }

    /// <summary>
    /// Gets the names of steps this step depends on
    /// </summary>
    IEnumerable<string> DependsOn { get; }

    /// <summary>
    /// Executes the step
    /// </summary>
    Task<StepResult> ExecuteAsync(
        WorkflowContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a step execution
/// </summary>
public record StepResult(
    bool IsSuccess,
    string? ErrorMessage = null);
