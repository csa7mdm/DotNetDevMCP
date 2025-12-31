// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Interfaces;
using DotNetDevMCP.Core.Models;
using DotNetDevMCP.Orchestration;
using DotNetDevMCP.Testing.DotNetTest;
using System.Diagnostics;

namespace DotNetDevMCP.Testing;

/// <summary>
/// Main service for parallel test execution leveraging OrchestrationService
/// </summary>
public class TestingService
{
    private readonly IOrchestrationService _orchestration;
    private readonly TestingServiceConfig _config;
    private readonly DotNetTestDiscoveryService _discoveryService;
    private readonly DotNetTestExecutorService _executorService;

    public TestingService()
        : this(new OrchestrationService(), TestingServiceConfig.Default)
    {
    }

    public TestingService(IOrchestrationService orchestration, TestingServiceConfig config)
    {
        _orchestration = orchestration ?? throw new ArgumentNullException(nameof(orchestration));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _discoveryService = new DotNetTestDiscoveryService();
        _executorService = new DotNetTestExecutorService();

        // Configure resource manager based on config
        _orchestration.ResourceManager.MaxConcurrency = config.MaxParallelTests;
    }

    /// <summary>
    /// Discover tests from an assembly
    /// </summary>
    public async Task<IEnumerable<TestCase>> DiscoverTestsAsync(
        string assemblyPath,
        TestDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await _discoveryService.DiscoverAsync(assemblyPath, options, cancellationToken);
    }

    /// <summary>
    /// Execute tests in parallel using orchestration components
    /// </summary>
    public async Task<TestRunSummary> RunTestsAsync(
        IEnumerable<TestCase> testCases,
        TestExecutionOptions? options = null,
        IProgress<TestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new TestExecutionOptions();
        var testList = testCases.ToList();

        if (!testList.Any())
        {
            return new TestRunSummary(0, 0, 0, 0, TimeSpan.Zero, Array.Empty<TestResult>());
        }

        var aggregator = new TestResultAggregator();
        var stopwatch = Stopwatch.StartNew();

        // Report initial progress
        ReportProgress(progress, aggregator, testList.Count, null);

        try
        {
            switch (opts.Strategy)
            {
                case TestExecutionStrategy.Sequential:
                    await ExecuteSequentialAsync(testList, opts, aggregator, progress, cancellationToken);
                    break;

                case TestExecutionStrategy.FullParallel:
                    await ExecuteFullParallelAsync(testList, opts, aggregator, progress, cancellationToken);
                    break;

                case TestExecutionStrategy.AssemblyLevelParallel:
                    await ExecuteAssemblyParallelAsync(testList, opts, aggregator, progress, cancellationToken);
                    break;

                case TestExecutionStrategy.SmartParallel:
                default:
                    await ExecuteSmartParallelAsync(testList, opts, aggregator, progress, cancellationToken);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested - return what we have
            stopwatch.Stop();
            return aggregator.GetFinalSummary();
        }

        stopwatch.Stop();
        var summary = aggregator.GetFinalSummary();

        // Report final progress
        ReportProgress(progress, aggregator, testList.Count, null);

        return summary;
    }

    private async Task ExecuteSequentialAsync(
        List<TestCase> testCases,
        TestExecutionOptions options,
        TestResultAggregator aggregator,
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken)
    {
        foreach (var testCase in testCases)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            ReportProgress(progress, aggregator, testCases.Count, testCase.DisplayName);
            var result = await ExecuteTestAsync(testCase, options, cancellationToken);
            aggregator.AddResult(result);
        }
    }

    private async Task ExecuteFullParallelAsync(
        List<TestCase> testCases,
        TestExecutionOptions options,
        TestResultAggregator aggregator,
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var testOperations = testCases.Select<TestCase, Func<CancellationToken, Task<TestResult>>>(tc => async (CancellationToken ct) =>
        {
            ReportProgress(progress, aggregator, testCases.Count, tc.DisplayName);
            var result = await ExecuteTestAsync(tc, options, ct);
            aggregator.AddResult(result);
            return result;
        });

        var concurrentOptions = new ConcurrentExecutionOptions(
            MaxDegreeOfParallelism: options.MaxParallelTests ?? _config.MaxParallelTests,
            ContinueOnError: options.ContinueOnFailure,
            OperationTimeout: options.DefaultTestTimeout ?? _config.DefaultTestTimeout);

        // Use ConcurrentExecutor directly for full parallel execution
        var executor = new ConcurrentExecutor();
        await executor.ExecuteAsync(testOperations, concurrentOptions, cancellationToken);
    }

    private async Task ExecuteAssemblyParallelAsync(
        List<TestCase> testCases,
        TestExecutionOptions options,
        TestResultAggregator aggregator,
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Group tests by assembly
        var assemblies = testCases.GroupBy(tc => tc.AssemblyPath).ToList();

        // Execute assemblies in parallel, tests within assembly sequentially
        var assemblyOperations = assemblies.Select<IGrouping<string, TestCase>, Func<CancellationToken, Task<string>>>(group => async (CancellationToken ct) =>
        {
            foreach (var testCase in group)
            {
                if (ct.IsCancellationRequested)
                    break;

                ReportProgress(progress, aggregator, testCases.Count, testCase.DisplayName);
                var result = await ExecuteTestAsync(testCase, options, ct);
                aggregator.AddResult(result);
            }
            return group.Key;
        });

        var concurrentOptions = new ConcurrentExecutionOptions(
            MaxDegreeOfParallelism: options.MaxParallelTests ?? _config.MaxParallelTests,
            ContinueOnError: options.ContinueOnFailure);

        var executor = new ConcurrentExecutor();
        await executor.ExecuteAsync(assemblyOperations, concurrentOptions, cancellationToken);
    }

    private async Task ExecuteSmartParallelAsync(
        List<TestCase> testCases,
        TestExecutionOptions options,
        TestResultAggregator aggregator,
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Group tests into fast and slow based on expected duration
        var fastTests = testCases.Where(tc => !tc.ExpectedDuration.HasValue || tc.ExpectedDuration.Value < TimeSpan.FromSeconds(1)).ToList();
        var slowTests = testCases.Where(tc => tc.ExpectedDuration.HasValue && tc.ExpectedDuration.Value >= TimeSpan.FromSeconds(1)).ToList();

        // Run slow tests first in parallel to maximize throughput
        if (slowTests.Any())
        {
            await ExecuteFullParallelAsync(slowTests, options, aggregator, progress, cancellationToken);
        }

        // Then run fast tests in parallel
        if (fastTests.Any() && !cancellationToken.IsCancellationRequested)
        {
            await ExecuteFullParallelAsync(fastTests, options, aggregator, progress, cancellationToken);
        }
    }

    private async Task<TestResult> ExecuteTestAsync(
        TestCase testCase,
        TestExecutionOptions options,
        CancellationToken cancellationToken)
    {
        // Execute the test using the real test executor
        return await _executorService.ExecuteAsync(testCase, options, cancellationToken);
    }

    private static void ReportProgress(
        IProgress<TestProgress>? progress,
        TestResultAggregator aggregator,
        int totalTests,
        string? currentTest)
    {
        var summary = aggregator.GetCurrentSummary();
        progress?.Report(new TestProgress(
            TotalTests: totalTests,
            CompletedTests: aggregator.CompletedCount,
            PassedTests: summary.PassedTests,
            FailedTests: summary.FailedTests,
            SkippedTests: summary.SkippedTests,
            CurrentTest: currentTest));
    }
}
