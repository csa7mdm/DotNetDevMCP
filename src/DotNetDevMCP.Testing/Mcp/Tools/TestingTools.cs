// Copyright (c) 2025 Ahmed Mustafa

using ModelContextProtocol;
using DotNetDevMCP.Core.Models;
using DotNetDevMCP.Testing;
using System.Text.Json;

namespace DotNetDevMCP.Testing.Mcp.Tools;

/// <summary>
/// Marker class for ILogger category specific to TestingTools
/// </summary>
public class TestingToolsLogCategory { }

/// <summary>
/// MCP Tools for test discovery and execution
/// </summary>
[McpServerToolType]
public static partial class TestingTools
{
    [McpServerTool(Name = "dotnet_test_discover", Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Discovers tests in a .NET assembly or project. Returns a list of all discovered test cases with their metadata.")]
    public static async Task<object> DiscoverTests(
        TestingService testingService,
        ILogger<TestingToolsLogCategory> logger,
        [Description("Path to the test assembly or project file")] string assemblyPath,
        [Description("Optional filter by test name")] string? nameFilter = null,
        [Description("Optional filter by category")] string? categoryFilter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Discovering tests in: {AssemblyPath}", assemblyPath);
            
            var options = new TestDiscoveryOptions(
                NameFilter: nameFilter,
                CategoryFilter: categoryFilter);
            
            var testCases = await testingService.DiscoverTestsAsync(assemblyPath, options, cancellationToken);
            var testList = testCases.ToList();
            
            logger.LogInformation("Discovered {Count} tests", testList.Count);
            
            return new
            {
                Success = true,
                TotalTests = testList.Count,
                Tests = testList.Select(t => new
                {
                    t.FullyQualifiedName,
                    t.DisplayName,
                    t.Framework,
                    t.AssemblyPath,
                    t.Category,
                    t.IsSkipped,
                    t.SkipReason,
                    ExpectedDuration = t.ExpectedDuration?.TotalSeconds,
                    t.Traits
                })
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover tests in {AssemblyPath}", assemblyPath);
            return new { Success = false, Error = ex.Message, TotalTests = 0, Tests = Array.Empty<object>() };
        }
    }

    [McpServerTool(Name = "dotnet_test_run", Idempotent = false, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Executes tests with configurable parallelism strategy. Supports sequential, full parallel, assembly-level parallel, and smart parallel execution.")]
    public static async Task<object> RunTests(
        TestingService testingService,
        ILogger<TestingToolsLogCategory> logger,
        [Description("List of test fully qualified names to run. If empty, runs all discovered tests.")] IEnumerable<string> testNames,
        [Description("Path to the test assembly or project file")] string assemblyPath,
        [Description("Execution strategy: Sequential, FullParallel, AssemblyLevelParallel, SmartParallel")] string strategy = "SmartParallel",
        [Description("Maximum number of parallel tests (default: processor count)")] int? maxParallelTests = null,
        [Description("Test timeout in seconds (default: 60)")] int? timeoutSeconds = null,
        [Description("Continue executing tests after a failure")] bool continueOnFailure = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Running tests with strategy: {Strategy}", strategy);
            
            // Parse strategy
            var executionStrategy = strategy.ToLower() switch
            {
                "sequential" => TestExecutionStrategy.Sequential,
                "fullparallel" => TestExecutionStrategy.FullParallel,
                "assemblylevelparallel" => TestExecutionStrategy.AssemblyLevelParallel,
                _ => TestExecutionStrategy.SmartParallel
            };
            
            // Discover tests if no specific names provided
            IEnumerable<TestCase> testCases;
            if (!testNames.Any())
            {
                var allTests = await testingService.DiscoverTestsAsync(assemblyPath, cancellationToken: cancellationToken);
                testCases = allTests;
            }
            else
            {
                var allTests = await testingService.DiscoverTestsAsync(assemblyPath, cancellationToken: cancellationToken);
                testCases = allTests.Where(t => testNames.Contains(t.FullyQualifiedName));
            }
            
            var options = new TestExecutionOptions(
                Strategy: executionStrategy,
                MaxParallelTests: maxParallelTests,
                DefaultTestTimeout: timeoutSeconds.HasValue ? TimeSpan.FromSeconds(timeoutSeconds.Value) : null,
                ContinueOnFailure: continueOnFailure);
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var summary = await testingService.RunTestsAsync(testCases, options, cancellationToken: cancellationToken);
            stopwatch.Stop();
            
            logger.LogInformation("Test run completed: {Passed}/{Total} passed in {Duration}s", 
                summary.PassedTests, summary.TotalTests, summary.TotalDuration.TotalSeconds);
            
            return new
            {
                Success = true,
                TotalTests = summary.TotalTests,
                PassedTests = summary.PassedTests,
                FailedTests = summary.FailedTests,
                SkippedTests = summary.SkippedTests,
                PassRate = Math.Round(summary.PassRate, 2),
                DurationSeconds = Math.Round(summary.TotalDuration.TotalSeconds, 2),
                Failures = summary.FailedResults.Select(f => new
                {
                    f.TestCase.FullyQualifiedName,
                    f.TestCase.DisplayName,
                    f.ErrorMessage,
                    f.StackTrace,
                    DurationSeconds = f.Duration.TotalSeconds
                })
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run tests");
            return new 
            { 
                Success = false, 
                Error = ex.Message,
                TotalTests = 0,
                PassedTests = 0,
                FailedTests = 0,
                SkippedTests = 0
            };
        }
    }

    [McpServerTool(Name = "dotnet_test_run_solution", Idempotent = false, ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Runs tests across an entire solution with parallel execution support and optional code coverage collection.")]
    public static async Task<object> RunSolutionTests(
        TestingService testingService,
        ILogger<TestingToolsLogCategory> logger,
        [Description("Path to the solution file (.sln)")] string solutionPath,
        [Description("Run tests in parallel across projects")] bool runInParallel = true,
        [Description("Maximum degree of parallelism")] int? maxDegreeOfParallelism = null,
        [Description("Filter expression for test selection")] string? filter = null,
        [Description("Collect code coverage data")] bool collectCoverage = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Running solution-level tests for: {SolutionPath}", solutionPath);
            
            // Find all test projects in solution
            var solutionDir = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory;
            var testProjects = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories)
                .Where(p => 
                {
                    var content = File.ReadAllText(p);
                    return content.Contains("<IsTestProject>true</IsTestProject>") ||
                           content.Contains("Microsoft.NET.Test.Sdk") ||
                           content.Contains("xunit") ||
                           content.Contains("NUnit") ||
                           content.Contains("MSTest");
                })
                .ToList();
            
            logger.LogInformation("Found {Count} test projects", testProjects.Count);
            
            var allResults = new List<TestResult>();
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            if (runInParallel && testProjects.Count > 1)
            {
                // Run tests in parallel across projects
                var projectTasks = testProjects.Select(async projectPath =>
                {
                    try
                    {
                        var tests = await testingService.DiscoverTestsAsync(projectPath, cancellationToken: cancellationToken);
                        var options = new TestExecutionOptions(
                            Strategy: TestExecutionStrategy.SmartParallel,
                            MaxParallelTests: maxDegreeOfParallelism ?? Environment.ProcessorCount,
                            ContinueOnFailure: true);
                        
                        return await testingService.RunTestsAsync(tests, options, cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to run tests in project: {ProjectPath}", projectPath);
                        return new TestRunSummary(0, 0, 0, 0, TimeSpan.Zero, Array.Empty<TestResult>());
                    }
                });
                
                var projectResults = await Task.WhenAll(projectTasks);
                allResults.AddRange(projectResults.SelectMany(r => r.Results));
            }
            else
            {
                // Run tests sequentially across projects
                foreach (var projectPath in testProjects)
                {
                    try
                    {
                        var tests = await testingService.DiscoverTestsAsync(projectPath, cancellationToken: cancellationToken);
                        var options = new TestExecutionOptions(
                            Strategy: TestExecutionStrategy.SmartParallel,
                            ContinueOnFailure: true);
                        
                        var result = await testingService.RunTestsAsync(tests, options, cancellationToken: cancellationToken);
                        allResults.AddRange(result.Results);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to run tests in project: {ProjectPath}", projectPath);
                    }
                }
            }
            
            totalStopwatch.Stop();
            
            var summary = new TestRunSummary(
                allResults.Count,
                allResults.Count(r => r.IsPassed),
                allResults.Count(r => r.IsFailed),
                allResults.Count(r => r.IsSkipped),
                totalStopwatch.Elapsed,
                allResults);
            
            logger.LogInformation("Solution test run completed: {Passed}/{Total} passed", 
                summary.PassedTests, summary.TotalTests);
            
            return new
            {
                Success = true,
                TotalTests = summary.TotalTests,
                PassedTests = summary.PassedTests,
                FailedTests = summary.FailedTests,
                SkippedTests = summary.SkippedTests,
                PassRate = Math.Round(summary.PassRate, 2),
                DurationSeconds = Math.Round(summary.TotalDuration.TotalSeconds, 2),
                TestProjects = testProjects.Count,
                CoverageCollected = collectCoverage,
                Failures = summary.FailedResults.Take(10).Select(f => new
                {
                    f.TestCase.FullyQualifiedName,
                    f.TestCase.DisplayName,
                    f.ErrorMessage,
                    ProjectPath = f.TestCase.AssemblyPath
                })
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run solution tests");
            return new 
            { 
                Success = false, 
                Error = ex.Message,
                TotalTests = 0,
                PassedTests = 0,
                FailedTests = 0,
                SkippedTests = 0
            };
        }
    }
}
