// Copyright (c) 2025 Ahmed Mustafa

namespace DotNetDevMCP.Core.Models;

public enum TestOutcome
{
    Passed,
    Failed,
    Skipped,
    NotRun
}

/// <summary>One test as reported by `dotnet test --list-tests`.</summary>
public record TestCase(string FullyQualifiedName, string ProjectPath);

/// <summary>One test result parsed from a TRX file.</summary>
public record TestResult(
    string FullyQualifiedName,
    string DisplayName,
    TestOutcome Outcome,
    TimeSpan Duration,
    string? ErrorMessage = null,
    string? StackTrace = null,
    string? Output = null);

/// <summary>Result of one `dotnet test` invocation (a project or a whole solution).</summary>
public record TestRunSummary(
    int TotalTests,
    int PassedTests,
    int FailedTests,
    int SkippedTests,
    TimeSpan Duration,
    IReadOnlyList<TestResult> Results,
    /// <summary>Set when `dotnet test` itself failed (build error, bad path) and produced no results.</summary>
    string? Error = null)
{
    public bool Success => Error is null && FailedTests == 0;
    public IEnumerable<TestResult> Failures => Results.Where(r => r.Outcome == TestOutcome.Failed);

    public static TestRunSummary Failed(string error, TimeSpan duration) => new(0, 0, 0, 0, duration, [], error);

    public static TestRunSummary Merge(IEnumerable<TestRunSummary> parts)
    {
        var list = parts.ToList();
        var errors = list.Where(p => p.Error is not null).Select(p => p.Error).ToList();
        return new(
            list.Sum(p => p.TotalTests), list.Sum(p => p.PassedTests), list.Sum(p => p.FailedTests), list.Sum(p => p.SkippedTests),
            list.Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(list.Max(p => p.Duration.Ticks)),
            list.SelectMany(p => p.Results).ToList(),
            errors.Count == 0 ? null : string.Join("\n", errors));
    }
}

/// <summary>A test method that (transitively) references a changed symbol.</summary>
public record AffectedTest(string FullyQualifiedName, string ProjectPath, string Via);

/// <summary>
/// Tests reached from a change. Complete = false means the reference walk ran out of its time budget: the change reaches too much
/// code to trace cheaply, Tests is a partial set, and every test should run instead.
/// </summary>
public record AffectedTestSelection(IReadOnlyList<AffectedTest> Tests, bool Complete, int SymbolsSearched);
