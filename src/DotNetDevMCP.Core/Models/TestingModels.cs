// Copyright (c) 2025 Ahmed Mustafa

namespace DotNetDevMCP.Core.Models;

/// <summary>
/// Supported test frameworks
/// </summary>
public enum TestFramework
{
    Unknown,
    XUnit,
    NUnit,
    MSTest
}

/// <summary>
/// Test execution outcome
/// </summary>
public enum TestOutcome
{
    Passed,
    Failed,
    Skipped,
    NotRun
}

/// <summary>
/// Test execution strategy
/// </summary>
public enum TestExecutionStrategy
{
    Sequential,
    FullParallel,
    AssemblyLevelParallel,
    SmartParallel
}

/// <summary>
/// Represents a discovered test case
/// </summary>
public record TestCase(
    string FullyQualifiedName,
    string DisplayName,
    TestFramework Framework,
    string AssemblyPath,
    string? Category = null,
    bool IsSkipped = false,
    string? SkipReason = null,
    TimeSpan? ExpectedDuration = null,
    Dictionary<string, string>? Traits = null);

/// <summary>
/// Result of executing a single test
/// </summary>
public record TestResult(
    TestCase TestCase,
    TestOutcome Outcome,
    TimeSpan Duration,
    string? ErrorMessage = null,
    string? StackTrace = null,
    string? Output = null)
{
    public bool IsPassed => Outcome == TestOutcome.Passed;
    public bool IsFailed => Outcome == TestOutcome.Failed;
    public bool IsSkipped => Outcome == TestOutcome.Skipped;
}

/// <summary>
/// Summary of a complete test run
/// </summary>
public record TestRunSummary(
    int TotalTests,
    int PassedTests,
    int FailedTests,
    int SkippedTests,
    TimeSpan TotalDuration,
    IEnumerable<TestResult> Results)
{
    public double PassRate => TotalTests > 0
        ? (double)PassedTests / TotalTests * 100
        : 0;

    public int NotRunTests => TotalTests - (PassedTests + FailedTests + SkippedTests);

    public IEnumerable<TestResult> FailedResults => Results.Where(r => r.IsFailed);
    public IEnumerable<TestResult> PassedResults => Results.Where(r => r.IsPassed);
    public IEnumerable<TestResult> SkippedResults => Results.Where(r => r.IsSkipped);
}

/// <summary>
/// Progress information during test execution
/// </summary>
public record TestProgress(
    int TotalTests,
    int CompletedTests,
    int PassedTests,
    int FailedTests,
    int SkippedTests,
    string? CurrentTest = null)
{
    public double PercentComplete => TotalTests > 0
        ? (double)CompletedTests / TotalTests * 100
        : 0;
}

/// <summary>
/// Options for test discovery
/// </summary>
public record TestDiscoveryOptions(
    string? NameFilter = null,
    string? CategoryFilter = null,
    Dictionary<string, string>? TraitFilters = null,
    bool IncludeSkippedTests = true);

/// <summary>
/// Options for test execution
/// </summary>
public record TestExecutionOptions(
    TestExecutionStrategy Strategy = TestExecutionStrategy.SmartParallel,
    int? MaxParallelTests = null,
    TimeSpan? DefaultTestTimeout = null,
    bool ContinueOnFailure = true,
    bool CaptureOutput = true,
    bool FailFast = false);

/// <summary>
/// Configuration for the testing service
/// </summary>
public record TestingServiceConfig(
    int MaxParallelTests,
    TimeSpan DefaultTestTimeout,
    TestExecutionStrategy DefaultStrategy,
    bool ContinueOnFailure,
    bool CaptureOutput)
{
    public static TestingServiceConfig Default => new(
        MaxParallelTests: Environment.ProcessorCount,
        DefaultTestTimeout: TimeSpan.FromMinutes(1),
        DefaultStrategy: TestExecutionStrategy.SmartParallel,
        ContinueOnFailure: true,
        CaptureOutput: true);
}

// Solution-level testing models

/// <summary>
/// Result of discovering tests in a solution
/// </summary>
public record TestDiscoveryResult(
    int TotalTests,
    IEnumerable<TestProject> TestProjects);

/// <summary>
/// Represents a test project in a solution
/// </summary>
public record TestProject(
    string ProjectPath,
    string TestFramework,
    int TestCount,
    IEnumerable<string> TestNames);

/// <summary>
/// Options for solution-level test execution
/// </summary>
public record SolutionTestExecutionOptions(
    bool RunInParallel = true,
    int? MaxDegreeOfParallelism = null,
    string? Filter = null,
    bool CollectCoverage = false);

/// <summary>
/// Result of executing tests at solution level
/// </summary>
public record TestExecutionResult(
    int TotalTests,
    int PassedTests,
    int FailedTests,
    int SkippedTests,
    TimeSpan Duration,
    IEnumerable<TestFailure> Failures);

/// <summary>
/// Represents a test failure
/// </summary>
public record TestFailure(
    string TestName,
    string ProjectPath,
    string Message,
    string? StackTrace);

/// <summary>
/// Code coverage analysis results
/// </summary>
public record CoverageAnalysis(
    double LineCoverage,
    double BranchCoverage,
    IEnumerable<UncoveredFile> UncoveredFiles);

/// <summary>
/// Represents a file with coverage information
/// </summary>
public record UncoveredFile(
    string FilePath,
    int TotalLines,
    int CoveredLines,
    double CoveragePercentage);
