// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DotNetDevMCP.Testing.DotNetTest;

/// <summary>
/// Discovers tests using the dotnet test command
/// </summary>
public class DotNetTestDiscoveryService
{
    /// <summary>
    /// Discover all tests in the specified assembly using dotnet test --list-tests
    /// </summary>
    public async Task<IEnumerable<TestCase>> DiscoverAsync(
        string assemblyPath,
        TestDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Test assembly not found: {assemblyPath}");
        }

        var testCases = new List<TestCase>();

        // Use dotnet test --list-tests to discover tests
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"test \"{assemblyPath}\" --no-build --list-tests",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new List<string>();
        var errors = new List<string>();

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                output.Add(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errors.Add(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var errorMessage = string.Join(Environment.NewLine, errors);
            throw new InvalidOperationException(
                $"Failed to discover tests: {errorMessage}");
        }

        // Parse the output to extract test names
        bool parsingTests = false;
        foreach (var line in output)
        {
            if (line.Contains("The following Tests are available:"))
            {
                parsingTests = true;
                continue;
            }

            if (parsingTests && line.Trim().Length > 0 && !line.StartsWith("Test run for"))
            {
                var testName = line.Trim();

                // Create TestCase from the fully qualified test name
                var testCase = CreateTestCase(testName, assemblyPath);

                // Apply filters if provided
                if (options != null && !PassesFilter(testCase, options))
                {
                    continue;
                }

                testCases.Add(testCase);
            }
        }

        return testCases;
    }

    private static TestCase CreateTestCase(string fullyQualifiedName, string assemblyPath)
    {
        // Extract display name (last part after the last dot)
        var lastDotIndex = fullyQualifiedName.LastIndexOf('.');
        var displayName = lastDotIndex >= 0
            ? fullyQualifiedName.Substring(lastDotIndex + 1)
            : fullyQualifiedName;

        return new TestCase(
            FullyQualifiedName: fullyQualifiedName,
            DisplayName: fullyQualifiedName, // Use full name for xUnit
            Framework: TestFramework.XUnit,
            AssemblyPath: assemblyPath,
            Category: null,
            IsSkipped: false,
            SkipReason: null,
            Traits: new Dictionary<string, string>());
    }

    private static bool PassesFilter(TestCase testCase, TestDiscoveryOptions options)
    {
        // Name filter
        if (options.NameFilter != null &&
            !testCase.DisplayName.Contains(options.NameFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Category filter
        if (options.CategoryFilter != null &&
            testCase.Category != options.CategoryFilter)
        {
            return false;
        }

        // Include skipped tests filter
        if (!options.IncludeSkippedTests && testCase.IsSkipped)
        {
            return false;
        }

        return true;
    }
}
