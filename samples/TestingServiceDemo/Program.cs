// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;
using DotNetDevMCP.Testing;
using System.Diagnostics;

namespace DotNetDevMCP.Samples.TestingServiceDemo;

/// <summary>
/// Comprehensive demo of the TestingService with real test execution
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("DotNetDevMCP Testing Service Integration Demo");
        Console.WriteLine("Real Test Discovery and Execution with Multiple Strategies");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        // Find the Core.Tests assembly
        var testAssemblyPath = FindTestAssembly();

        if (testAssemblyPath == null)
        {
            Console.WriteLine("ERROR: Could not find DotNetDevMCP.Core.Tests.dll");
            Console.WriteLine("Please build the solution first: dotnet build");
            return;
        }

        Console.WriteLine($"Found test assembly: {Path.GetFileName(testAssemblyPath)}");
        Console.WriteLine();

        // Create the Testing Service
        var testingService = new TestingService();

        // Run all demos
        await RunDemo1_DiscoverAndExecuteAll(testingService, testAssemblyPath);
        await RunDemo2_SequentialExecution(testingService, testAssemblyPath);
        await RunDemo3_FullParallelExecution(testingService, testAssemblyPath);
        await RunDemo4_AssemblyParallelExecution(testingService, testAssemblyPath);
        await RunDemo5_SmartParallelExecution(testingService, testAssemblyPath);
        await RunDemo6_FilteredExecution(testingService, testAssemblyPath);

        Console.WriteLine();
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("All demos completed successfully!");
        Console.WriteLine("=".PadRight(80, '='));
    }

    private static async Task RunDemo1_DiscoverAndExecuteAll(TestingService service, string assemblyPath)
    {
        Console.WriteLine("DEMO 1: Discover and Execute All Tests");
        Console.WriteLine("-".PadRight(80, '-'));

        // Discover all tests
        var sw = Stopwatch.StartNew();
        var tests = await service.DiscoverTestsAsync(assemblyPath);
        sw.Stop();

        var testList = tests.ToList();
        Console.WriteLine($"Discovered {testList.Count} tests in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine();

        // Show first 5 tests
        Console.WriteLine("Sample tests:");
        foreach (var test in testList.Take(5))
        {
            Console.WriteLine($"  • {test.DisplayName}");
        }
        if (testList.Count > 5)
        {
            Console.WriteLine($"  ... and {testList.Count - 5} more");
        }
        Console.WriteLine();
    }

    private static async Task RunDemo2_SequentialExecution(TestingService service, string assemblyPath)
    {
        Console.WriteLine("DEMO 2: Sequential Execution (ConcurrentExecutor tests)");
        Console.WriteLine("-".PadRight(80, '-'));

        // Discover filtered tests
        var tests = await service.DiscoverTestsAsync(
            assemblyPath,
            new TestDiscoveryOptions(NameFilter: "ConcurrentExecutor"));

        var testList = tests.Take(5).ToList();
        Console.WriteLine($"Running {testList.Count} tests sequentially...");
        Console.WriteLine();

        var progress = new ProgressReporter();
        var sw = Stopwatch.StartNew();

        var options = new TestExecutionOptions(
            Strategy: TestExecutionStrategy.Sequential,
            MaxParallelTests: 1);

        var summary = await service.RunTestsAsync(testList, options, progress);
        sw.Stop();

        Console.WriteLine();
        PrintSummary(summary, sw.Elapsed);
        Console.WriteLine();
    }

    private static async Task RunDemo3_FullParallelExecution(TestingService service, string assemblyPath)
    {
        Console.WriteLine("DEMO 3: Full Parallel Execution (ConcurrentExecutor tests)");
        Console.WriteLine("-".PadRight(80, '-'));

        var tests = await service.DiscoverTestsAsync(
            assemblyPath,
            new TestDiscoveryOptions(NameFilter: "ConcurrentExecutor"));

        var testList = tests.Take(5).ToList();
        Console.WriteLine($"Running {testList.Count} tests in full parallel...");
        Console.WriteLine();

        var progress = new ProgressReporter();
        var sw = Stopwatch.StartNew();

        var options = new TestExecutionOptions(
            Strategy: TestExecutionStrategy.FullParallel,
            MaxParallelTests: 5);

        var summary = await service.RunTestsAsync(testList, options, progress);
        sw.Stop();

        Console.WriteLine();
        PrintSummary(summary, sw.Elapsed);
        Console.WriteLine();
    }

    private static async Task RunDemo4_AssemblyParallelExecution(TestingService service, string assemblyPath)
    {
        Console.WriteLine("DEMO 4: Assembly-Level Parallel Execution");
        Console.WriteLine("-".PadRight(80, '-'));

        var tests = await service.DiscoverTestsAsync(assemblyPath);
        var testList = tests.Take(10).ToList();

        Console.WriteLine($"Running {testList.Count} tests with assembly-level parallelism...");
        Console.WriteLine();

        var progress = new ProgressReporter();
        var sw = Stopwatch.StartNew();

        var options = new TestExecutionOptions(
            Strategy: TestExecutionStrategy.AssemblyLevelParallel,
            MaxParallelTests: 3);

        var summary = await service.RunTestsAsync(testList, options, progress);
        sw.Stop();

        Console.WriteLine();
        PrintSummary(summary, sw.Elapsed);
        Console.WriteLine();
    }

    private static async Task RunDemo5_SmartParallelExecution(TestingService service, string assemblyPath)
    {
        Console.WriteLine("DEMO 5: Smart Parallel Execution (optimized for test duration)");
        Console.WriteLine("-".PadRight(80, '-'));

        var tests = await service.DiscoverTestsAsync(assemblyPath);
        var testList = tests.Take(10).ToList();

        Console.WriteLine($"Running {testList.Count} tests with smart parallelism...");
        Console.WriteLine();

        var progress = new ProgressReporter();
        var sw = Stopwatch.StartNew();

        var options = new TestExecutionOptions(
            Strategy: TestExecutionStrategy.SmartParallel,
            MaxParallelTests: 4);

        var summary = await service.RunTestsAsync(testList, options, progress);
        sw.Stop();

        Console.WriteLine();
        PrintSummary(summary, sw.Elapsed);
        Console.WriteLine();
    }

    private static async Task RunDemo6_FilteredExecution(TestingService service, string assemblyPath)
    {
        Console.WriteLine("DEMO 6: Filtered Test Execution (ResourceManager tests)");
        Console.WriteLine("-".PadRight(80, '-'));

        // Discover only ResourceManager tests
        var tests = await service.DiscoverTestsAsync(
            assemblyPath,
            new TestDiscoveryOptions(NameFilter: "ResourceManager"));

        var testList = tests.ToList();
        Console.WriteLine($"Found {testList.Count} ResourceManager tests");
        Console.WriteLine($"Running tests in parallel...");
        Console.WriteLine();

        var progress = new ProgressReporter();
        var sw = Stopwatch.StartNew();

        var options = new TestExecutionOptions(
            Strategy: TestExecutionStrategy.FullParallel,
            MaxParallelTests: 4);

        var summary = await service.RunTestsAsync(testList, options, progress);
        sw.Stop();

        Console.WriteLine();
        PrintSummary(summary, sw.Elapsed);

        // Show individual results
        if (summary.Results.Any(r => r.IsFailed))
        {
            Console.WriteLine();
            Console.WriteLine("Failed tests:");
            foreach (var result in summary.Results.Where(r => r.IsFailed))
            {
                Console.WriteLine($"  ✗ {result.TestCase.DisplayName}");
                Console.WriteLine($"    Error: {result.ErrorMessage}");
            }
        }

        Console.WriteLine();
    }

    private static void PrintSummary(TestRunSummary summary, TimeSpan wallClockTime)
    {
        Console.WriteLine("Test Run Summary:");
        Console.WriteLine($"  Total Tests:    {summary.TotalTests}");
        Console.WriteLine($"  Passed:         {summary.PassedTests} ({GetPercentage(summary.PassedTests, summary.TotalTests)}%)");
        Console.WriteLine($"  Failed:         {summary.FailedTests} ({GetPercentage(summary.FailedTests, summary.TotalTests)}%)");
        Console.WriteLine($"  Skipped:        {summary.SkippedTests} ({GetPercentage(summary.SkippedTests, summary.TotalTests)}%)");
        Console.WriteLine($"  Duration:       {summary.TotalDuration.TotalMilliseconds:F0}ms");
        Console.WriteLine($"  Wall Clock:     {wallClockTime.TotalMilliseconds:F0}ms");

        if (wallClockTime.TotalMilliseconds > 0)
        {
            var speedup = summary.TotalDuration.TotalMilliseconds / wallClockTime.TotalMilliseconds;
            Console.WriteLine($"  Speedup:        {speedup:F2}x");
        }
    }

    private static int GetPercentage(int value, int total)
    {
        return total == 0 ? 0 : (int)Math.Round(100.0 * value / total);
    }

    private static string? FindTestAssembly()
    {
        var possiblePaths = new[]
        {
            "../../tests/DotNetDevMCP.Core.Tests/bin/Debug/net9.0/DotNetDevMCP.Core.Tests.dll",
            "../../../tests/DotNetDevMCP.Core.Tests/bin/Debug/net9.0/DotNetDevMCP.Core.Tests.dll",
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "tests", "DotNetDevMCP.Core.Tests", "bin", "Debug", "net9.0", "DotNetDevMCP.Core.Tests.dll"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "tests", "DotNetDevMCP.Core.Tests", "bin", "Debug", "net9.0", "DotNetDevMCP.Core.Tests.dll")
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}

/// <summary>
/// Simple progress reporter for demo purposes
/// </summary>
internal class ProgressReporter : IProgress<TestProgress>
{
    private int _lastReported = -1;

    public void Report(TestProgress progress)
    {
        // Only report on completion changes to avoid too much output
        if (progress.CompletedTests != _lastReported)
        {
            _lastReported = progress.CompletedTests;
            var percentage = progress.TotalTests == 0 ? 0 : (100 * progress.CompletedTests / progress.TotalTests);
            Console.Write($"\r  Progress: {progress.CompletedTests}/{progress.TotalTests} ({percentage}%) - " +
                         $"Passed: {progress.PassedTests}, Failed: {progress.FailedTests}  ");
        }
    }
}
