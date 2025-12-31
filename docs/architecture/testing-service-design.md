# Testing Service Design

## Overview

The Testing Service provides high-performance parallel test execution for .NET test frameworks (xUnit, NUnit, MSTest). It leverages the orchestration components to achieve 50-80% faster test runs compared to sequential execution.

## Goals

1. **Performance**: Execute tests in parallel to maximize throughput
2. **Framework Support**: Support major .NET test frameworks (xUnit, NUnit, MSTest)
3. **Reliability**: Aggregate results accurately, handle failures gracefully
4. **Intelligence**: Smart test ordering, dependency detection, failure prediction
5. **Integration**: Seamless integration with orchestration components

## Architecture

### Component Hierarchy

```
TestingService (coordinator)
    ├── ITestDiscoveryService (find tests)
    ├── ITestExecutorService (run tests)
    ├── ITestResultAggregator (collect results)
    └── OrchestrationService (parallel execution)
```

### Key Components

#### 1. TestDiscoveryService
Discovers tests from assemblies using reflection or framework-specific APIs.

**Responsibilities**:
- Load test assemblies
- Discover test classes and methods
- Extract test metadata (traits, categories, skip reasons)
- Support filtering (by name, category, trait)

**Output**: Collection of `TestCase` objects

#### 2. TestExecutorService
Executes individual tests or test batches.

**Responsibilities**:
- Invoke test framework runners
- Capture test output (stdout, stderr, logs)
- Measure execution time
- Handle test isolation (AppDomain, Process)

**Output**: `TestResult` per test

#### 3. TestResultAggregator
Collects and aggregates test results from parallel execution.

**Responsibilities**:
- Thread-safe result collection
- Calculate summary statistics
- Group failures by type
- Generate reports (XML, JSON, console)

**Output**: `TestRunSummary`

#### 4. TestingService (Main Coordinator)
Orchestrates the entire test execution workflow.

**Responsibilities**:
- Coordinate discovery → execution → aggregation
- Apply parallel execution strategy
- Manage resource allocation
- Report progress
- Handle cancellation

## Data Models

### TestCase
```csharp
public record TestCase(
    string FullyQualifiedName,      // Namespace.Class.Method
    string DisplayName,              // Human-readable name
    TestFramework Framework,         // xUnit, NUnit, MSTest
    string? Category,                // Test category/trait
    bool IsSkipped,                  // Skip flag
    string? SkipReason,              // Why skipped
    TimeSpan? ExpectedDuration);     // Historical average
```

### TestResult
```csharp
public record TestResult(
    TestCase TestCase,
    TestOutcome Outcome,             // Passed, Failed, Skipped
    TimeSpan Duration,
    string? ErrorMessage,
    string? StackTrace,
    string? Output);                 // Console output

public enum TestOutcome
{
    Passed,
    Failed,
    Skipped,
    NotRun
}
```

### TestRunSummary
```csharp
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
}
```

## Execution Strategies

### Strategy 1: Full Parallel
Execute all tests concurrently with resource throttling.

**Pros**: Maximum speed
**Cons**: May cause resource contention, test interference
**Use When**: Tests are fully isolated, system has ample resources

### Strategy 2: Assembly-Level Parallel
Execute test assemblies in parallel, tests within assembly sequentially.

**Pros**: Good isolation, fewer conflicts
**Cons**: Less parallelization if few assemblies
**Use When**: Tests share state within assembly

### Strategy 3: Smart Parallel
Group tests by characteristics (fast/slow, category, resource usage) and execute groups optimally.

**Pros**: Balanced performance and reliability
**Cons**: Requires test metadata
**Use When**: Have historical test data

## Integration with Orchestration

### Using ConcurrentExecutor
```csharp
var testOperations = testCases.Select(tc => async (ct) =>
    await _testExecutor.ExecuteAsync(tc, ct));

var result = await _orchestration.ConcurrentExecutor.ExecuteAsync(
    testOperations,
    new ConcurrentExecutionOptions(
        MaxDegreeOfParallelism: _maxParallelTests,
        ContinueOnError: true,  // Continue even if tests fail
        OperationTimeout: TimeSpan.FromMinutes(5)),
    cancellationToken);
```

### Using ResourceManager
```csharp
// Throttle test execution to prevent resource exhaustion
var testResult = await _orchestration.ResourceManager
    .ExecuteWithThrottlingAsync(
        async () => await RunTestAsync(testCase),
        cancellationToken);
```

### Using WorkflowEngine
For complex test scenarios with dependencies:
```csharp
var workflow = new TestWorkflow("Integration Tests")
    .AddStep("SetupDatabase", SetupDatabaseAsync)
    .AddStep("RunDatabaseTests", RunTestsAsync, dependsOn: new[] { "SetupDatabase" })
    .AddStep("CleanupDatabase", CleanupDatabaseAsync, dependsOn: new[] { "RunDatabaseTests" });

await _orchestration.ExecuteWorkflowAsync(workflow);
```

## Performance Targets

| Scenario | Sequential Time | Parallel Time | Improvement |
|----------|----------------|---------------|-------------|
| 100 unit tests (avg 100ms each) | 10s | 1-2s | 80-90% |
| 50 integration tests (avg 1s each) | 50s | 10-15s | 70-80% |
| Mixed suite (500 tests) | 60s | 12-18s | 70-80% |

## Test Framework Integration

### xUnit
Use `Xunit.Runner.Utility` for discovery and execution:
```csharp
using Xunit.Runners;

var assemblyRunner = AssemblyRunner.WithoutAppDomain(assemblyPath);
assemblyRunner.OnDiscoveryComplete += OnDiscoveryComplete;
assemblyRunner.OnExecutionComplete += OnExecutionComplete;
```

### NUnit
Use `NUnit.Engine` for discovery and execution:
```csharp
using NUnit.Engine;

var engine = TestEngineActivator.CreateInstance();
var package = new TestPackage(assemblyPath);
using var runner = engine.GetRunner(package);
```

### MSTest
Use `Microsoft.VisualStudio.TestPlatform.ObjectModel` for discovery:
```csharp
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
```

## Progress Reporting

```csharp
public record TestProgress(
    int TotalTests,
    int CompletedTests,
    int PassedTests,
    int FailedTests,
    string? CurrentTest);

// Usage
var progress = new Progress<TestProgress>(p =>
    Console.WriteLine($"[{p.CompletedTests}/{p.TotalTests}] Running: {p.CurrentTest}"));

await _testingService.RunTestsAsync(testCases, progress, cancellationToken);
```

## Error Handling

1. **Test Failures**: Collect and report, continue execution
2. **Framework Errors**: Retry once, then mark as failed
3. **Timeouts**: Configurable per-test timeout, force termination
4. **Resource Errors**: Throttle execution, wait for resources

## Future Enhancements

1. **Test Prioritization**: Run recently failed tests first
2. **Predictive Execution**: Skip tests unlikely to fail based on code changes
3. **Distributed Execution**: Run tests across multiple machines
4. **Code Coverage**: Integrate with coverage tools
5. **Flaky Test Detection**: Automatically detect and report flaky tests
6. **Test Impact Analysis**: Run only tests affected by code changes

## Configuration

```csharp
public class TestingServiceOptions
{
    public int MaxParallelTests { get; set; } = Environment.ProcessorCount;
    public TimeSpan DefaultTestTimeout { get; set; } = TimeSpan.FromMinutes(1);
    public TestExecutionStrategy Strategy { get; set; } = TestExecutionStrategy.SmartParallel;
    public bool ContinueOnFailure { get; set; } = true;
    public bool CaptureOutput { get; set; } = true;
}
```

## Implementation Plan

1. ✅ Design document (this document)
2. ⏳ Core interfaces (`ITestDiscoveryService`, `ITestExecutorService`, etc.)
3. ⏳ Data models (`TestCase`, `TestResult`, `TestRunSummary`)
4. ⏳ TDD: Write tests for TestDiscoveryService
5. ⏳ Implement TestDiscoveryService
6. ⏳ TDD: Write tests for TestExecutorService
7. ⏳ Implement TestExecutorService
8. ⏳ TDD: Write tests for TestingService coordinator
9. ⏳ Implement TestingService with orchestration integration
10. ⏳ Integration tests with real test projects
11. ⏳ Performance benchmarks

## Success Criteria

- ✅ Support xUnit, NUnit, and MSTest
- ✅ Achieve 50-80% speedup on parallel-safe test suites
- ✅ Accurate result aggregation (100% reliability)
- ✅ Graceful handling of test failures and timeouts
- ✅ Progress reporting during execution
- ✅ Clean integration with OrchestrationService

## See Also

- [Orchestration Design](orchestration-design.md)
- [System Overview](system-overview.md)
- OrchestrationService implementation
