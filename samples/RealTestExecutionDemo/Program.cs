// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;
using DotNetDevMCP.Testing.DotNetTest;
using System.Diagnostics;

namespace DotNetDevMCP.Samples.RealTestExecutionDemo;

/// <summary>
/// Demonstrates discovering and running real xUnit tests from compiled assemblies
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("DotNetDevMCP Real Test Execution Demo");
        Console.WriteLine("Discovering and Running Actual xUnit Tests");
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

        await RunDemo1_DiscoverTests(testAssemblyPath);
        await RunDemo2_ExecuteSingleTest(testAssemblyPath);
        await RunDemo3_ExecuteFilteredTests(testAssemblyPath);
        await RunDemo4_ExecuteBatchTests(testAssemblyPath);

        Console.WriteLine();
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("Demo completed successfully!");
        Console.WriteLine("=".PadRight(80, '='));
    }

    private static async Task RunDemo1_DiscoverTests(string assemblyPath)
    {
        Console.WriteLine("DEMO 1: Discover All Tests");
        Console.WriteLine("-".PadRight(80, '-'));

        var discoveryService = new DotNetTestDiscoveryService();

        Console.WriteLine("Discovering tests...");
        var sw = Stopwatch.StartNew();
        var testCases = await discoveryService.DiscoverAsync(assemblyPath);
        sw.Stop();

        var testList = testCases.ToList();

        Console.WriteLine($"Discovery completed in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Found {testList.Count} tests");
        Console.WriteLine();

        // Show first 10 tests
        Console.WriteLine("Sample tests discovered:");
        foreach (var test in testList.Take(10))
        {
            var skipIndicator = test.IsSkipped ? " [SKIPPED]" : "";
            Console.WriteLine($"  • {test.DisplayName}{skipIndicator}");
        }

        if (testList.Count > 10)
        {
            Console.WriteLine($"  ... and {testList.Count - 10} more tests");
        }

        Console.WriteLine();
    }

    private static async Task RunDemo2_ExecuteSingleTest(string assemblyPath)
    {
        Console.WriteLine("DEMO 2: Execute a Single Test");
        Console.WriteLine("-".PadRight(80, '-'));

        var discoveryService = new DotNetTestDiscoveryService();
        var executorService = new DotNetTestExecutorService();

        // Discover tests
        var testCases = await discoveryService.DiscoverAsync(assemblyPath);
        var testList = testCases.ToList();

        if (!testList.Any())
        {
            Console.WriteLine("No tests found to execute");
            return;
        }

        // Pick a test that should pass
        var testToRun = testList
            .FirstOrDefault(t => t.DisplayName.Contains("ExecuteAsync_WithSuccessfulOperations"))
            ?? testList.First();

        Console.WriteLine($"Running test: {testToRun.DisplayName}");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var result = await executorService.ExecuteAsync(testToRun);
        sw.Stop();

        Console.WriteLine($"Execution completed in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Test Result: {result.Outcome}");
        Console.WriteLine($"Test Duration: {result.Duration.TotalMilliseconds:F2}ms");

        if (result.IsFailed)
        {
            Console.WriteLine($"Error: {result.ErrorMessage}");
            if (!string.IsNullOrEmpty(result.StackTrace))
            {
                Console.WriteLine("Stack Trace:");
                Console.WriteLine(result.StackTrace);
            }
        }

        if (!string.IsNullOrEmpty(result.Output))
        {
            Console.WriteLine("Output:");
            Console.WriteLine(result.Output);
        }

        Console.WriteLine();
    }

    private static async Task RunDemo3_ExecuteFilteredTests(string assemblyPath)
    {
        Console.WriteLine("DEMO 3: Execute Filtered Tests");
        Console.WriteLine("-".PadRight(80, '-'));

        var discoveryService = new DotNetTestDiscoveryService();
        var executorService = new DotNetTestExecutorService();

        // Discover only ConcurrentExecutor tests
        Console.WriteLine("Discovering ConcurrentExecutor tests...");
        var testCases = await discoveryService.DiscoverAsync(
            assemblyPath,
            new TestDiscoveryOptions(NameFilter: "ConcurrentExecutor"));

        var testList = testCases.ToList();
        Console.WriteLine($"Found {testList.Count} matching tests");
        Console.WriteLine();

        if (!testList.Any())
        {
            Console.WriteLine("No matching tests found");
            return;
        }

        // Execute first 3 tests
        var testsToRun = testList.Take(3).ToList();
        Console.WriteLine($"Running {testsToRun.Count} tests...");
        Console.WriteLine();

        var passed = 0;
        var failed = 0;
        var totalDuration = TimeSpan.Zero;

        foreach (var test in testsToRun)
        {
            var result = await executorService.ExecuteAsync(test);
            totalDuration += result.Duration;

            var status = result.IsPassed ? "✓ PASS" : "✗ FAIL";
            Console.WriteLine($"  {status} {test.DisplayName} ({result.Duration.TotalMilliseconds:F0}ms)");

            if (result.IsPassed)
                passed++;
            else
                failed++;
        }

        Console.WriteLine();
        Console.WriteLine($"Summary: {passed} passed, {failed} failed");
        Console.WriteLine($"Total Duration: {totalDuration.TotalMilliseconds:F0}ms");
        Console.WriteLine();
    }

    private static async Task RunDemo4_ExecuteBatchTests(string assemblyPath)
    {
        Console.WriteLine("DEMO 4: Execute Tests in Batch");
        Console.WriteLine("-".PadRight(80, '-'));

        var discoveryService = new DotNetTestDiscoveryService();
        var executorService = new DotNetTestExecutorService();

        // Discover ResourceManager tests
        var testCases = await discoveryService.DiscoverAsync(
            assemblyPath,
            new TestDiscoveryOptions(NameFilter: "ResourceManager"));

        var testList = testCases.Take(5).ToList();

        if (!testList.Any())
        {
            Console.WriteLine("No tests found");
            return;
        }

        Console.WriteLine($"Executing {testList.Count} tests in batch...");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var results = await executorService.ExecuteBatchAsync(testList);
        sw.Stop();

        var resultList = results.ToList();
        var passed = resultList.Count(r => r.IsPassed);
        var failed = resultList.Count(r => r.IsFailed);
        var skipped = resultList.Count(r => r.IsSkipped);

        Console.WriteLine("Results:");
        foreach (var result in resultList)
        {
            var icon = result.IsPassed ? "✓" : result.IsFailed ? "✗" : "⊘";
            Console.WriteLine($"  {icon} {result.TestCase.DisplayName} ({result.Duration.TotalMilliseconds:F0}ms)");
        }

        Console.WriteLine();
        Console.WriteLine($"Total Duration: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Results: {passed} passed, {failed} failed, {skipped} skipped");
        Console.WriteLine();
    }

    private static string? FindTestAssembly()
    {
        // Try to find the test assembly in common locations
        var possiblePaths = new[]
        {
            // Relative to samples directory
            "../../tests/DotNetDevMCP.Core.Tests/bin/Debug/net9.0/DotNetDevMCP.Core.Tests.dll",
            "../../../tests/DotNetDevMCP.Core.Tests/bin/Debug/net9.0/DotNetDevMCP.Core.Tests.dll",

            // Absolute from workspace root
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "tests", "DotNetDevMCP.Core.Tests", "bin", "Debug", "net9.0", "DotNetDevMCP.Core.Tests.dll"),

            // Check if we're in the bin directory
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
