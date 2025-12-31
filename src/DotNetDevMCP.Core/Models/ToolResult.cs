// Copyright (c) 2025 Ahmed Mustafa

namespace DotNetDevMCP.Core.Models;

/// <summary>
/// Represents the result of an MCP tool execution
/// </summary>
public class ToolResult
{
    public bool IsSuccess { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }

    public static ToolResult Success(string content, Dictionary<string, object>? metadata = null)
        => new() { IsSuccess = true, Content = content, Metadata = metadata };

    public static ToolResult Failure(string error, Dictionary<string, object>? metadata = null)
        => new() { IsSuccess = false, Error = error, Metadata = metadata };
}
