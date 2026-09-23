using System.Xml.Linq;
using DotNetDevMCP.Core.Models;
using DotNetDevMCP.Testing;

namespace DotNetDevMCP.Testing.Tests;

public class TestRunnerTrxTests
{
    private const string Trx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="a" testName="Adds" outcome="Passed" duration="00:00:00.0120000" />
            <UnitTestResult testId="b" testName="Divides(x: 1)" outcome="Failed" duration="00:00:01.5000000">
              <Output>
                <StdOut>some console output</StdOut>
                <ErrorInfo>
                  <Message>Expected 2 but got 3</Message>
                  <StackTrace>   at Calc.Tests.CalcTests.Divides()</StackTrace>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
            <UnitTestResult testId="c" testName="Skipped" outcome="NotExecuted" duration="00:00:00" />
          </Results>
          <TestDefinitions>
            <UnitTest id="a" name="Adds"><TestMethod className="Calc.Tests.CalcTests" name="Adds" /></UnitTest>
            <UnitTest id="b" name="Divides(x: 1)"><TestMethod className="Calc.Tests.CalcTests" name="Divides" /></UnitTest>
            <UnitTest id="c" name="Skipped"><TestMethod className="Calc.Tests.Outer+Nested" name="Skipped" /></UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    [Fact]
    public void ParseTrx_maps_outcomes_names_and_errors()
    {
        var s = TestRunner.ParseTrx(XDocument.Parse(Trx));

        Assert.Equal(3, s.TotalTests);
        Assert.Equal(1, s.PassedTests);
        Assert.Equal(1, s.FailedTests);
        Assert.Equal(1, s.SkippedTests);
        Assert.False(s.Success);

        var failed = Assert.Single(s.Failures);
        Assert.Equal("Calc.Tests.CalcTests.Divides", failed.FullyQualifiedName);
        Assert.Equal("Divides(x: 1)", failed.DisplayName);
        Assert.Equal("Expected 2 but got 3", failed.ErrorMessage);
        Assert.Contains("CalcTests.Divides", failed.StackTrace);
        Assert.Equal("some console output", failed.Output);
        Assert.Equal(TimeSpan.FromSeconds(1.5), failed.Duration);

        Assert.Equal("Calc.Tests.Outer+Nested.Skipped", s.Results[2].FullyQualifiedName);
        Assert.Equal(TestOutcome.Skipped, s.Results[2].Outcome);
    }

    [Fact]
    public void Merge_sums_counts_and_concatenates_errors()
    {
        var ok = TestRunner.ParseTrx(XDocument.Parse(Trx));
        var broken = TestRunSummary.Failed("build failed", TimeSpan.FromSeconds(2));

        var m = TestRunSummary.Merge([ok, broken]);

        Assert.Equal(3, m.TotalTests);
        Assert.Equal("build failed", m.Error);
        Assert.False(m.Success);
        Assert.Equal(TimeSpan.FromSeconds(2), m.Duration); // max, not sum: runs are concurrent
    }

    [Fact]
    public void UnfinishedModules_reports_only_modules_that_started_without_a_matching_end_line()
    {
        var output = """
            Running tests from /repo/bin/Debug/net10.0/Polly.Core.Tests.dll
            Running tests from /repo/bin/Debug/net10.0/Polly.Specs.dll
            /repo/bin/Debug/net10.0/Polly.Core.Tests.dll (net10.0|x64) passed! - Failed: 0, Passed: 120, Skipped: 0, Total: 120, Duration: 4s
            """;

        var unfinished = TestRunner.UnfinishedModules(output);

        Assert.Equal(["/repo/bin/Debug/net10.0/Polly.Specs.dll"], unfinished);
    }

    [Fact]
    public void UnfinishedModules_is_empty_when_every_started_module_also_finished()
    {
        var output = """
            Running tests from /repo/bin/Debug/net10.0/Polly.Core.Tests.dll
            /repo/bin/Debug/net10.0/Polly.Core.Tests.dll (net10.0|x64) failed! - Failed: 1, Passed: 119, Skipped: 0, Total: 120, Duration: 4s
            """;

        Assert.Empty(TestRunner.UnfinishedModules(output));
    }

    [Fact]
    public void UnfinishedModules_tolerates_output_with_no_recognizable_lines()
    {
        Assert.Empty(TestRunner.UnfinishedModules("some unrelated build output\nwith random lines\nand no module markers at all"));
    }
}
