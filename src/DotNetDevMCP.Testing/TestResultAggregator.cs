// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DotNetDevMCP.Testing;

/// <summary>
/// Thread-safe aggregator for collecting test results from parallel execution
/// </summary>
public class TestResultAggregator
{
    private readonly ConcurrentBag<TestResult> _results = new();
    private readonly Stopwatch _stopwatch = new();
    private int _totalTests;
    private int _passedTests;
    private int _failedTests;
    private int _skippedTests;

    public TestResultAggregator()
    {
        _stopwatch.Start();
    }

    /// <summary>
    /// Add a test result (thread-safe)
    /// </summary>
    public void AddResult(TestResult result)
    {
        _results.Add(result);

        switch (result.Outcome)
        {
            case TestOutcome.Passed:
                Interlocked.Increment(ref _passedTests);
                break;
            case TestOutcome.Failed:
                Interlocked.Increment(ref _failedTests);
                break;
            case TestOutcome.Skipped:
                Interlocked.Increment(ref _skippedTests);
                break;
        }

        Interlocked.Increment(ref _totalTests);
    }

    /// <summary>
    /// Get current summary without stopping the timer
    /// </summary>
    public TestRunSummary GetCurrentSummary()
    {
        return new TestRunSummary(
            TotalTests: _totalTests,
            PassedTests: _passedTests,
            FailedTests: _failedTests,
            SkippedTests: _skippedTests,
            TotalDuration: _stopwatch.Elapsed,
            Results: _results.ToList());
    }

    /// <summary>
    /// Get final summary and stop the timer
    /// </summary>
    public TestRunSummary GetFinalSummary()
    {
        _stopwatch.Stop();
        return GetCurrentSummary();
    }

    /// <summary>
    /// Clear all results and reset the timer
    /// </summary>
    public void Clear()
    {
        _results.Clear();
        _totalTests = 0;
        _passedTests = 0;
        _failedTests = 0;
        _skippedTests = 0;
        _stopwatch.Restart();
    }

    /// <summary>
    /// Get the number of completed tests
    /// </summary>
    public int CompletedCount => _totalTests;

    /// <summary>
    /// Get the current elapsed time
    /// </summary>
    public TimeSpan Elapsed => _stopwatch.Elapsed;
}
