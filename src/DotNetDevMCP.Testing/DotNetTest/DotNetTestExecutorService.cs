// Copyright (c) 2025 Ahmed Mustafa

using DotNetDevMCP.Core.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DotNetDevMCP.Testing.DotNetTest;

/// <summary>
/// Executes tests using the dotnet test command
/// </summary>
public class DotNetTestExecutorService
{
    /// <summary>
    /// Execute a single test using dotnet test --filter
    /// </summary>
    public async Task<TestResult> ExecuteAsync(
        TestCase testCase,
        TestExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Use dotnet test with --filter to run a specific test
            var filter = $"FullyQualifiedName={testCase.FullyQualifiedName}";
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test \"{testCase.AssemblyPath}\" --no-build --filter \"{filter}\"",
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
            stopwatch.Stop();

            // Parse the output to determine test result
            var outcome = ParseTestOutcome(output, process.ExitCode);
            var errorMessage = ParseErrorMessage(output);
            var testOutput = string.Join(Environment.NewLine, output);

            return new TestResult(
                TestCase: testCase,
                Outcome: outcome,
                Duration: stopwatch.Elapsed,
                ErrorMessage: errorMessage,
                StackTrace: null,
                Output: testOutput);
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
        var results = new List<TestResult>();
        var testList = testCases.ToList();

        if (!testList.Any())
        {
            return results;
        }

        // Execute tests individually for now
        // TODO: Optimize by running multiple tests in one dotnet test command
        int completed = 0;
        int passed = 0;
        int failed = 0;
        int skipped = 0;

        foreach (var testCase in testList)
        {
            var result = await ExecuteAsync(testCase, options, cancellationToken);
            results.Add(result);

            completed++;
            if (result.IsPassed) passed++;
            else if (result.IsFailed) failed++;
            else if (result.IsSkipped) skipped++;

            progress?.Report(new TestProgress(
                TotalTests: testList.Count,
                CompletedTests: completed,
                PassedTests: passed,
                FailedTests: failed,
                SkippedTests: skipped,
                CurrentTest: testCase.DisplayName));
        }

        return results;
    }

    private static TestOutcome ParseTestOutcome(List<string> output, int exitCode)
    {
        var outputText = string.Join(" ", output);

        if (outputText.Contains("Passed!") || exitCode == 0)
        {
            return TestOutcome.Passed;
        }

        if (outputText.Contains("Failed!") || outputText.Contains("Error Message:"))
        {
            return TestOutcome.Failed;
        }

        if (outputText.Contains("Skipped!"))
        {
            return TestOutcome.Skipped;
        }

        return exitCode == 0 ? TestOutcome.Passed : TestOutcome.Failed;
    }

    private static string? ParseErrorMessage(List<string> output)
    {
        // Look for error message in output
        for (int i = 0; i < output.Count; i++)
        {
            if (output[i].Contains("Error Message:") && i + 1 < output.Count)
            {
                return output[i + 1].Trim();
            }
        }

        // Check for failed test indication
        var failedLine = output.FirstOrDefault(l => l.Contains("Failed"));
        if (failedLine != null)
        {
            return failedLine.Trim();
        }

        return null;
    }
}
