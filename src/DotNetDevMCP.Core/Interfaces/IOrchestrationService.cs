// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;

namespace DotNetDevMCP.Core.Interfaces;

/// <summary>
/// Service for orchestrating concurrent operations across multiple tools
/// </summary>
public interface IOrchestrationService
{
    /// <summary>
    /// Executes multiple tools in parallel
    /// </summary>
    Task<IEnumerable<ToolResult>> ExecuteParallelAsync(
        IEnumerable<(string toolName, string arguments)> operations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a workflow with sequential and parallel steps
    /// </summary>
    Task<ToolResult> ExecuteWorkflowAsync(
        IWorkflow workflow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Monitors and manages resource allocation for concurrent operations
    /// </summary>
    IResourceManager ResourceManager { get; }
}

// IWorkflow and IWorkflowStep moved to IWorkflowEngine.cs
// IResourceManager moved to IResourceManager.cs
