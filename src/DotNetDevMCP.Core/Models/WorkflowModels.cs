// Copyright (c) 2025 Ahmed Mustafa

namespace DotNetDevMCP.Core.Models;

/// <summary>
/// Result of workflow execution
/// </summary>
public record WorkflowExecutionResult(
    bool IsSuccess,
    IEnumerable<StepExecutionResult> StepResults,
    WorkflowContext FinalContext,
    TimeSpan Duration)
{
    public bool HasErrors => StepResults.Any(r => !r.IsSuccess);
    public int TotalSteps => StepResults.Count();
    public int SuccessfulSteps => StepResults.Count(r => r.IsSuccess);
    public int FailedSteps => StepResults.Count(r => !r.IsSuccess);
}

/// <summary>
/// Result of a single workflow step execution
/// </summary>
public record StepExecutionResult(
    string StepName,
    bool IsSuccess,
    string? Error,
    TimeSpan Duration);

/// <summary>
/// Progress information for workflow execution
/// </summary>
public record WorkflowProgress(
    int TotalSteps,
    int CompletedSteps,
    string? CurrentStepName)
{
    public double PercentComplete => TotalSteps > 0
        ? (double)CompletedSteps / TotalSteps * 100
        : 0;
}

/// <summary>
/// Context passed between workflow steps
/// </summary>
public class WorkflowContext
{
    private readonly Dictionary<string, object> _state = new();

    /// <summary>
    /// Gets all state keys
    /// </summary>
    public IEnumerable<string> Keys => _state.Keys;

    /// <summary>
    /// Sets a value in the context
    /// </summary>
    public void Set<T>(string key, T value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        _state[key] = value;
    }

    /// <summary>
    /// Gets a value from the context
    /// </summary>
    public T? Get<T>(string key)
    {
        if (_state.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// Tries to get a value from the context
    /// </summary>
    public bool TryGet<T>(string key, out T? value)
    {
        if (_state.TryGetValue(key, out var objValue) && objValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Checks if a key exists in the context
    /// </summary>
    public bool ContainsKey(string key) => _state.ContainsKey(key);

    /// <summary>
    /// Removes a key from the context
    /// </summary>
    public bool Remove(string key) => _state.Remove(key);

    /// <summary>
    /// Clears all state
    /// </summary>
    public void Clear() => _state.Clear();
}
