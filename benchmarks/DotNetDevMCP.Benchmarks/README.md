# DotNetDevMCP Performance Benchmarks

This project contains comprehensive performance benchmarks for the DotNetDevMCP orchestration components using BenchmarkDotNet.

## Purpose

Measure and validate the performance improvements achieved through concurrent operations and orchestration. Target: **50-80% improvement** over sequential execution.

## Benchmark Categories

### 1. OrchestrationBenchmarks
Compares different approaches to parallel execution:

- **Sequential_Baseline**: Traditional sequential processing (baseline)
- **Parallel_TaskWhenAll**: Basic Task.WhenAll parallelization
- **Parallel_ConcurrentExecutor_NoThrottle**: ConcurrentExecutor without throttling
- **Parallel_ConcurrentExecutor_Throttled**: ConcurrentExecutor with max parallelism of 5
- **ResourceManager_WithThrottling**: ResourceManager with concurrency limit
- **WorkflowEngine_WithDependencies**: WorkflowEngine with dependency management
- **OrchestrationService_FullStack**: Complete orchestration stack

**What to Expect**:
- Sequential baseline: ~1000ms for 20 operations @ 50ms each
- Parallel approaches: ~50-100ms (90-95% improvement)
- Throttled approaches: ~200-250ms (75-80% improvement with resource safety)

### 2. BatchOperationBenchmarks
Measures batch processing efficiency:

- **Sequential_Batch**: Process 100 operations sequentially
- **Concurrent_ResourceManager_Batch**: Process 100 operations with ResourceManager (concurrency: 10)

**What to Expect**:
- Sequential: ~1000ms
- Concurrent: ~100ms (90% improvement)

### 3. WorkflowBenchmarks
Evaluates workflow execution with dependencies:

- **Sequential_Workflow**: 10 sequential steps
- **Parallel_Workflow_WithEngine**: 10 steps with parallel opportunities

**What to Expect**:
- Sequential: ~200ms (10 steps @ 20ms each)
- Parallel workflow: ~100ms (50% improvement through parallelization)

## Running the Benchmarks

### Prerequisites
- .NET 9.0 SDK
- Release mode (required for accurate benchmarks)
- Elevated permissions may be required on some systems

### Run All Benchmarks
```bash
cd benchmarks/DotNetDevMCP.Benchmarks
dotnet run -c Release
```

### Run Specific Benchmark
```bash
# Run only OrchestrationBenchmarks
dotnet run -c Release --filter *OrchestrationBenchmarks*

# Run only BatchOperationBenchmarks
dotnet run -c Release --filter *BatchOperationBenchmarks*

# Run only WorkflowBenchmarks
dotnet run -c Release --filter *WorkflowBenchmarks*
```

## Understanding the Results

BenchmarkDotNet will output:

### Mean Time
Average execution time - **lower is better**

### Median Time
Middle value of all measurements - good indicator of typical performance

### Memory Allocation
Total memory allocated - **lower is better**

### Rank
Relative ranking compared to baseline

## Expected Performance Improvements

Based on the orchestration design targets:

| Scenario | Sequential Time | Parallel Time | Improvement |
|----------|----------------|---------------|-------------|
| 20 operations @ 50ms | ~1000ms | ~50-100ms | 90-95% |
| 100 operations @ 10ms | ~1000ms | ~100ms | 90% |
| 10-step workflow | ~200ms | ~100ms | 50% |

## Interpreting Results

### Good Performance
- Parallel approaches show 50-80% improvement over sequential
- Memory allocation is reasonable (< 10KB per operation)
- Throttled approaches prevent resource exhaustion

### Warning Signs
- Less than 50% improvement may indicate:
  - Operations are too fast (overhead dominates)
  - Insufficient parallelism opportunities
  - Resource contention issues

## Benchmark Output

Results are saved to `BenchmarkDotNet.Artifacts/results/`:
- Summary tables (Markdown, HTML, CSV)
- Detailed measurements
- Memory diagrams
- Performance charts

## Advanced Options

### Custom Configuration
```bash
# Run with profiler
dotnet run -c Release --profiler ETW

# Export to JSON
dotnet run -c Release --exporters json

# Run with different runtimes
dotnet run -c Release --runtimes net9.0 net8.0
```

### Benchmark Attributes

- `[MemoryDiagnoser]`: Track memory allocations
- `[Orderer(SummaryOrderPolicy.FastestToSlowest)]`: Sort by performance
- `[RankColumn]`: Show relative ranking
- `[Baseline = true]`: Mark baseline for comparison

## Continuous Performance Monitoring

These benchmarks should be run:
- Before and after major orchestration changes
- As part of CI/CD pipeline (on dedicated hardware)
- When investigating performance regressions
- To validate optimization efforts

## Performance Targets

DotNetDevMCP orchestration aims for:
- ✅ **50-80% improvement** for concurrent operations
- ✅ **< 5% overhead** for resource management
- ✅ **Linear scaling** up to max concurrency
- ✅ **Graceful degradation** under resource constraints

## See Also

- [OrchestrationDemo](../../samples/OrchestrationDemo/) - Live demonstration
- [Orchestration Design](../../docs/architecture/orchestration-design.md) - Architecture details
- [Test Suite](../../tests/DotNetDevMCP.Core.Tests/) - Unit and integration tests
