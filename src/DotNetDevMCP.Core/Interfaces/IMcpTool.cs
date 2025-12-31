// Original SharpTools concept by кɵɵѕнī - Extended for DotNetDevMCP
// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;

namespace DotNetDevMCP.Core.Interfaces;

/// <summary>
/// Base interface for all MCP tools in DotNetDevMCP
/// </summary>
public interface IMcpTool
{
    /// <summary>
    /// Gets the unique name of this tool (used in MCP protocol)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a brief description of what this tool does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the JSON schema for this tool's input parameters
    /// </summary>
    object InputSchema { get; }

    /// <summary>
    /// Executes the tool with the given arguments
    /// </summary>
    /// <param name="arguments">Tool arguments as JSON</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tool execution result</returns>
    Task<ToolResult> ExecuteAsync(string arguments, CancellationToken cancellationToken = default);
}
