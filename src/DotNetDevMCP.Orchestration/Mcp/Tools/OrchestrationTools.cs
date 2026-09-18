// Copyright (c) 2025 Ahmed Mustafa

using System.ComponentModel;
using System.Text.Json;
using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotNetDevMCP.Orchestration.Mcp.Tools;

/// <summary>
/// Log category marker for orchestration tools
/// </summary>
public sealed class OrchestrationToolsLogCategory { }

/// <summary>
/// MCP tools that run other tools of this server concurrently: a flat parallel batch,
/// or a dependency graph (DAG) where independent steps run in parallel.
/// </summary>
[McpServerToolType]
public static class OrchestrationTools
{
    [McpServerTool(Name = "orchestrate_parallel", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Runs several tools of this server concurrently (throttled by the resource manager) and returns every result. Use for independent operations, e.g. build two projects while running tests.")]
    // ponytail: arrays, not IEnumerable<T> - the SDK binds IEnumerable<T> parameters from DI and hides them from the schema.
    public static async Task<object> ExecuteParallel(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        IOrchestrationService orchestrationService,
        ILogger<OrchestrationToolsLogCategory> logger,
        [Description("Operations to run. Each has the tool name and its arguments object.")] ToolOperationInput[] operations,
        [Description("Maximum degree of parallelism (default: processor count)")] int? maxParallelism = null,
        CancellationToken cancellationToken = default)
    {
        var ops = operations.ToList();
        if (maxParallelism is > 0)
        {
            orchestrationService.ResourceManager.MaxConcurrency = maxParallelism.Value;
        }

        foreach (var name in ops.Select(o => o.ToolName).Distinct())
        {
            orchestrationService.RegisterTool(name, (args, ct) => InvokeServerToolAsync(server, context, name, args, ct));
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = (await orchestrationService.ExecuteParallelAsync(
            ops.Select(o => (o.ToolName, SerializeArgs(o.Arguments))),
            cancellationToken)).ToList();
        stopwatch.Stop();

        var failed = results.Count(r => !r.IsSuccess);
        logger.LogInformation("orchestrate_parallel: {Ok}/{Total} succeeded in {Sec:F2}s", results.Count - failed, results.Count, stopwatch.Elapsed.TotalSeconds);

        return new
        {
            Success = failed == 0,
            TotalOperations = results.Count,
            FailedOperations = failed,
            DurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2),
            Results = results.Select((r, i) => new { Index = i, ops[i].ToolName, r.IsSuccess, r.Content, r.Error }),
        };
    }

    [McpServerTool(Name = "execute_workflow", Idempotent = false, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Runs tools of this server as a dependency graph: steps whose dependencies are done run in parallel, dependents wait. Use for build -> test -> analyze pipelines.")]
    public static async Task<object> ExecuteWorkflow(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        IOrchestrationService orchestrationService,
        ILogger<OrchestrationToolsLogCategory> logger,
        [Description("Name of the workflow")] string workflowName,
        [Description("Steps. dependsOn lists step names that must finish first; steps with no unmet dependencies run in parallel.")] WorkflowStepInput[] steps,
        CancellationToken cancellationToken = default)
    {
        var workflow = new ToolWorkflow(
            workflowName,
            steps.Select(s => new ToolWorkflowStep(s, (args, ct) => InvokeServerToolAsync(server, context, s.ToolName, args, ct))).ToList());

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await orchestrationService.ExecuteWorkflowAsync(workflow, cancellationToken);
        stopwatch.Stop();

        logger.LogInformation("execute_workflow {Name}: success={Ok} in {Sec:F2}s", workflowName, result.IsSuccess, stopwatch.Elapsed.TotalSeconds);

        return new
        {
            result.IsSuccess,
            WorkflowName = workflowName,
            TotalSteps = workflow.StepList.Count,
            DurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2),
            Message = result.Content,
            result.Error,
            Steps = workflow.StepList.Select(s => new { s.Name, s.DependsOn, Result = s.LastResult?.Content, Error = s.LastResult?.Error }),
        };
    }

    [McpServerTool(Name = "get_resource_metrics", Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Returns current concurrency limits and how many orchestrated operations are running or queued.")]
    public static object GetResourceMetrics(IOrchestrationService orchestrationService)
        => orchestrationService.ResourceManager.GetMetrics();

    [McpServerTool(Name = "configure_resource_limits", Idempotent = true, ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Sets the maximum number of orchestrated operations that may run concurrently.")]
    public static object ConfigureResourceLimits(
        IOrchestrationService orchestrationService,
        [Description("Maximum number of concurrent operations (>= 1)")] int maxConcurrency)
    {
        if (maxConcurrency < 1) return new { Success = false, Error = "maxConcurrency must be >= 1" };
        orchestrationService.ResourceManager.MaxConcurrency = maxConcurrency;
        return new { Success = true, MaxConcurrency = maxConcurrency };
    }

    // --- dispatch into this server's own tool collection ---

    private static async Task<ToolResult> InvokeServerToolAsync(
        McpServer server, RequestContext<CallToolRequestParams> context, string toolName, string argsJson, CancellationToken ct)
    {
        var tools = server.ServerOptions.ToolCollection;
        if (tools is null || !tools.TryGetPrimitive(toolName, out var tool))
        {
            return ToolResult.Failure($"Tool '{toolName}' is not registered on this server");
        }

        var args = string.IsNullOrWhiteSpace(argsJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);

        var request = new RequestContext<CallToolRequestParams>(server, context.JsonRpcRequest, new CallToolRequestParams { Name = toolName, Arguments = args });
        try
        {
            var response = await tool.InvokeAsync(request, ct);
            var text = string.Join("\n", (response.Content ?? []).OfType<TextContentBlock>().Select(c => c.Text));
            if (response.StructuredContent is not null && string.IsNullOrEmpty(text))
            {
                text = response.StructuredContent.ToString() ?? string.Empty;
            }
            return response.IsError == true ? ToolResult.Failure(text) : ToolResult.Success(text);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"{toolName}: {ex.Message}");
        }
    }

    private static string SerializeArgs(Dictionary<string, JsonElement>? args)
        => args is null ? string.Empty : JsonSerializer.Serialize(args);
}

/// <summary>One operation for <c>orchestrate_parallel</c>.</summary>
public record ToolOperationInput(
    [property: Description("Name of a tool on this server")] string ToolName,
    [property: Description("Arguments object for that tool")] Dictionary<string, JsonElement>? Arguments = null);

/// <summary>One step for <c>execute_workflow</c>.</summary>
public record WorkflowStepInput(
    [property: Description("Unique step name")] string Name,
    [property: Description("Name of a tool on this server")] string ToolName,
    [property: Description("Arguments object for that tool")] Dictionary<string, JsonElement>? Arguments = null,
    [property: Description("Step names that must complete before this one")] IReadOnlyList<string>? DependsOn = null);

internal sealed class ToolWorkflow(string name, List<ToolWorkflowStep> steps) : IWorkflow
{
    public string Name => name;
    public List<ToolWorkflowStep> StepList => steps;
    public IEnumerable<IWorkflowStep> Steps => steps;
}

internal sealed class ToolWorkflowStep(WorkflowStepInput input, Func<string, CancellationToken, Task<ToolResult>> invoke) : IWorkflowStep
{
    public string Name => input.Name;
    public bool CanExecuteInParallel => true;
    public IEnumerable<string> DependsOn => input.DependsOn ?? [];
    public ToolResult? LastResult { get; private set; }

    public async Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default)
    {
        var argsJson = input.Arguments is null ? string.Empty : JsonSerializer.Serialize(input.Arguments);
        LastResult = await invoke(argsJson, cancellationToken);
        context.Set(input.Name, LastResult);
        return new StepResult(LastResult.IsSuccess, LastResult.Error);
    }
}
