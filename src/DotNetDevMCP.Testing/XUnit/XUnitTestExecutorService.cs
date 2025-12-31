// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace DotNetDevMCP.Testing.XUnit;

/// <summary>
/// Executes xUnit tests and captures results
/// </summary>
public class XUnitTestExecutorService
{
    /// <summary>
    /// Execute a single xUnit test
    /// </summary>
    public async Task<TestResult> ExecuteAsync(
        TestCase testCase,
        TestExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var executionSink = new TestExecutionSink();

            await Task.Run(() =>
            {
                // Change working directory to the test assembly location
                var originalDirectory = Directory.GetCurrentDirectory();
                var assemblyDirectory = Path.GetDirectoryName(testCase.AssemblyPath)
                    ?? throw new InvalidOperationException("Could not determine assembly directory");

                try
                {
                    Directory.SetCurrentDirectory(assemblyDirectory);

                    using var framework = new XunitFrontController(
                        AppDomainSupport.Denied,
                        testCase.AssemblyPath,
                        configFileName: null,
                        shadowCopy: true,
                        diagnosticMessageSink: new NullMessageSink());

                // Discover the specific test
                var discoveryOptions = TestFrameworkOptions.ForDiscovery();
                var discoverySink = new TestDiscoverySink();

                framework.Find(
                    includeSourceInformation: false,
                    discoverySink,
                    discoveryOptions);

                discoverySink.Finished.WaitOne();

                // Find the matching test case
                var xunitTestCase = discoverySink.TestCases
                    .FirstOrDefault(tc => tc.DisplayName == testCase.DisplayName);

                if (xunitTestCase == null)
                {
                    throw new InvalidOperationException(
                        $"Test case not found: {testCase.DisplayName}");
                }

                // Execute the test
                var executionOptions = TestFrameworkOptions.ForExecution();
                if (options?.DefaultTestTimeout != null)
                {
                    executionOptions.SetMaxParallelThreads(1);
                }

                framework.RunTests(
                    new[] { xunitTestCase },
                    executionSink,
                    executionOptions);

                executionSink.Finished.WaitOne();
                }
                finally
                {
                    Directory.SetCurrentDirectory(originalDirectory);
                }
            }, cancellationToken);

            stopwatch.Stop();

            // Convert xUnit result to our TestResult model
            var result = executionSink.TestResult;

            if (result == null)
            {
                return new TestResult(
                    TestCase: testCase,
                    Outcome: TestOutcome.NotRun,
                    Duration: stopwatch.Elapsed,
                    ErrorMessage: "Test did not produce a result");
            }

            return new TestResult(
                TestCase: testCase,
                Outcome: result.Outcome,
                Duration: TimeSpan.FromSeconds((double)result.ExecutionTime),
                ErrorMessage: result.ErrorMessage,
                StackTrace: result.ErrorStackTrace,
                Output: result.Output);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new TestResult(
                TestCase: testCase,
                Outcome: TestOutcome.Failed,
                Duration: stopwatch.Elapsed,
                ErrorMessage: "Test was cancelled");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new TestResult(
                TestCase: testCase,
                Outcome: TestOutcome.Failed,
                Duration: stopwatch.Elapsed,
                ErrorMessage: ex.Message,
                StackTrace: ex.StackTrace);
        }
    }

    /// <summary>
    /// Execute multiple tests from the same assembly
    /// </summary>
    public async Task<IEnumerable<TestResult>> ExecuteBatchAsync(
        IEnumerable<TestCase> testCases,
        TestExecutionOptions? options = null,
        IProgress<TestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var testList = testCases.ToList();
        var results = new List<TestResult>();

        if (!testList.Any())
        {
            return results;
        }

        var assemblyPath = testList.First().AssemblyPath;

        await Task.Run(() =>
        {
            // Change working directory to the test assembly location
            var originalDirectory = Directory.GetCurrentDirectory();
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath)
                ?? throw new InvalidOperationException("Could not determine assembly directory");

            try
            {
                Directory.SetCurrentDirectory(assemblyDirectory);

                var executionSink = new BatchTestExecutionSink(testList.Count);

                using var framework = new XunitFrontController(
                    AppDomainSupport.Denied,
                    assemblyPath,
                    configFileName: null,
                    shadowCopy: true,
                    diagnosticMessageSink: new NullMessageSink());

                // Discover all tests
                var discoveryOptions = TestFrameworkOptions.ForDiscovery();
                var discoverySink = new TestDiscoverySink();

                framework.Find(
                    includeSourceInformation: false,
                    discoverySink,
                    discoveryOptions);

                discoverySink.Finished.WaitOne();

                // Find matching test cases
                var xunitTestCases = testList
                    .Select(tc => discoverySink.TestCases
                        .FirstOrDefault(xtc => xtc.DisplayName == tc.DisplayName))
                    .Where(xtc => xtc != null)
                    .ToList();

                // Execute tests
                var executionOptions = TestFrameworkOptions.ForExecution();
                framework.RunTests(
                    xunitTestCases!,
                    executionSink,
                    executionOptions);

                executionSink.Finished.WaitOne();

                // Convert results
                foreach (var testCase in testList)
                {
                    var result = executionSink.Results
                        .FirstOrDefault(r => r.TestDisplayName == testCase.DisplayName);

                    if (result != null)
                    {
                        results.Add(new TestResult(
                            TestCase: testCase,
                            Outcome: result.Outcome,
                            Duration: TimeSpan.FromSeconds((double)result.ExecutionTime),
                            ErrorMessage: result.ErrorMessage,
                            StackTrace: result.ErrorStackTrace,
                            Output: result.Output));
                    }
                    else
                    {
                        results.Add(new TestResult(
                            TestCase: testCase,
                            Outcome: TestOutcome.NotRun,
                            Duration: TimeSpan.Zero,
                            ErrorMessage: "Test did not execute"));
                    }
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
            }
        }, cancellationToken);

        return results;
    }
}

/// <summary>
/// Captures execution results for a single test
/// </summary>
internal class TestExecutionSink : IMessageSink
{
    public ManualResetEvent Finished { get; } = new(false);
    public XUnitTestResult? TestResult { get; private set; }

    public bool OnMessage(IMessageSinkMessage message)
    {
        switch (message)
        {
            case ITestPassed passed:
                TestResult = new XUnitTestResult
                {
                    TestDisplayName = passed.Test.DisplayName,
                    Outcome = TestOutcome.Passed,
                    ExecutionTime = passed.ExecutionTime,
                    Output = passed.Output
                };
                break;

            case ITestFailed failed:
                TestResult = new XUnitTestResult
                {
                    TestDisplayName = failed.Test.DisplayName,
                    Outcome = TestOutcome.Failed,
                    ExecutionTime = failed.ExecutionTime,
                    Output = failed.Output,
                    ErrorMessage = failed.Messages.FirstOrDefault() ?? "Test failed",
                    ErrorStackTrace = failed.StackTraces.FirstOrDefault()
                };
                break;

            case ITestSkipped skipped:
                TestResult = new XUnitTestResult
                {
                    TestDisplayName = skipped.Test.DisplayName,
                    Outcome = TestOutcome.Skipped,
                    ExecutionTime = skipped.ExecutionTime,
                    ErrorMessage = skipped.Reason
                };
                break;

            case ITestAssemblyFinished:
                Finished.Set();
                break;
        }

        return true;
    }

    public void Dispose()
    {
        Finished.Dispose();
    }
}

/// <summary>
/// Captures execution results for multiple tests
/// </summary>
internal class BatchTestExecutionSink : IMessageSink
{
    private readonly int _expectedTests;
    private int _completedTests;

    public ManualResetEvent Finished { get; } = new(false);
    public List<XUnitTestResult> Results { get; } = new();

    public BatchTestExecutionSink(int expectedTests)
    {
        _expectedTests = expectedTests;
    }

    public bool OnMessage(IMessageSinkMessage message)
    {
        switch (message)
        {
            case ITestPassed passed:
                Results.Add(new XUnitTestResult
                {
                    TestDisplayName = passed.Test.DisplayName,
                    Outcome = TestOutcome.Passed,
                    ExecutionTime = passed.ExecutionTime,
                    Output = passed.Output
                });
                Interlocked.Increment(ref _completedTests);
                break;

            case ITestFailed failed:
                Results.Add(new XUnitTestResult
                {
                    TestDisplayName = failed.Test.DisplayName,
                    Outcome = TestOutcome.Failed,
                    ExecutionTime = failed.ExecutionTime,
                    Output = failed.Output,
                    ErrorMessage = failed.Messages.FirstOrDefault() ?? "Test failed",
                    ErrorStackTrace = failed.StackTraces.FirstOrDefault()
                });
                Interlocked.Increment(ref _completedTests);
                break;

            case ITestSkipped skipped:
                Results.Add(new XUnitTestResult
                {
                    TestDisplayName = skipped.Test.DisplayName,
                    Outcome = TestOutcome.Skipped,
                    ExecutionTime = skipped.ExecutionTime,
                    ErrorMessage = skipped.Reason
                });
                Interlocked.Increment(ref _completedTests);
                break;

            case ITestAssemblyFinished:
                Finished.Set();
                break;
        }

        return true;
    }

    public void Dispose()
    {
        Finished.Dispose();
    }
}

/// <summary>
/// Internal model for xUnit test results
/// </summary>
internal class XUnitTestResult
{
    public string TestDisplayName { get; set; } = string.Empty;
    public TestOutcome Outcome { get; set; }
    public decimal ExecutionTime { get; set; }
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
}
