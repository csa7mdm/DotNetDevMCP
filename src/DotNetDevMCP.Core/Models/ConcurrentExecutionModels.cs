// Copyright (c) 2025 Ahmed Mustafa

namespace DotNetDevMCP.Core.Models;

/// <summary>
/// Options for configuring concurrent execution
/// </summary>
public record ConcurrentExecutionOptions(
    int? MaxDegreeOfParallelism = null,
    bool ContinueOnError = true,
    TimeSpan? OperationTimeout = null);

/// <summary>
/// Result of concurrent execution containing both successes and errors
/// </summary>
public record ConcurrentExecutionResult<T>(
    IEnumerable<T> SuccessfulResults,
    IEnumerable<ExecutionError> Errors,
    int TotalOperations,
    int SuccessfulOperations,
    TimeSpan Duration)
{
    public bool HasErrors => Errors.Any();
    public bool AllSucceeded => !HasErrors;
    public double SuccessRate => TotalOperations > 0 ? (double)SuccessfulOperations / TotalOperations : 0;
}

/// <summary>
/// Represents an error that occurred during execution
/// </summary>
public record ExecutionError(
    int OperationIndex,
    Exception Exception,
    string Message);

/// <summary>
/// Progress information for concurrent execution
/// </summary>
public record ExecutionProgress(
    int TotalOperations,
    int CompletedOperations,
    int FailedOperations)
{
    public double PercentComplete => TotalOperations > 0
        ? (double)CompletedOperations / TotalOperations * 100
        : 0;
}
