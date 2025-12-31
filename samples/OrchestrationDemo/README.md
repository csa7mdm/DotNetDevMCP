# Orchestration Demo

This demo showcases the complete orchestration capabilities of DotNetDevMCP, demonstrating how ResourceManager, ConcurrentExecutor, WorkflowEngine, and OrchestrationService work together to provide high-performance concurrent operations.

## What This Demo Shows

### Demo 1: ResourceManager Throttling
- Executes 10 operations with max concurrency of 3
- Prevents resource exhaustion through intelligent throttling
- Tracks metrics including execution count, failures, and average execution time
- **Key Insight**: Operations are queued and executed as slots become available, never exceeding the configured limit

### Demo 2: ConcurrentExecutor with Error Handling
- Processes 8 file operations in parallel with max parallelism of 4
- Simulates 25% failure rate to demonstrate error handling
- Continues execution despite failures (ContinueOnError: true)
- **Key Insight**: Successful operations complete while failures are collected for reporting

### Demo 3: WorkflowEngine - Build Pipeline
- Executes a realistic build pipeline with 6 steps
- Demonstrates dependency management (Build depends on Restore)
- Shows parallel execution (Build_Core, Build_Services, Build_API run concurrently)
- Tracks progress throughout execution
- **Key Insight**: Complex workflows with dependencies execute in optimal order

### Demo 4: OrchestrationService - Tool Coordination
- Registers and executes development tools (compile, test, analyze)
- Combines ResourceManager throttling with parallel execution
- Demonstrates tool resolution and execution
- **Key Insight**: High-level coordination of multiple tools with resource management

### Demo 5: Complete CI/CD Pipeline
- Full end-to-end development workflow
- Source control → Restore → Parallel builds → Parallel tests → Quality checks → Package → Deploy
- Context passing between steps (commit hash, quality score)
- **Key Insight**: Real-world CI/CD scenario showing all components working together

## Running the Demo

### Prerequisites
- .NET 9.0 SDK or later
- DotNetDevMCP solution built

### Build and Run
```bash
# From solution root
dotnet build samples/OrchestrationDemo/OrchestrationDemo.csproj

# Run the demo
dotnet run --project samples/OrchestrationDemo/OrchestrationDemo.csproj
```

### Expected Output
The demo will run 5 demonstrations sequentially, showing:
- Timestamped operation execution
- Concurrent operation limiting
- Error handling and recovery
- Workflow step progression
- Performance metrics

## Performance Highlights

Based on the demo output:
- **Parallel Build Steps**: 3 build steps execute concurrently instead of sequentially
- **Resource Throttling**: 10 operations completed in ~788ms with max concurrency 3 (vs unlimited concurrency which could cause resource issues)
- **Error Resilience**: 6/8 operations succeed despite 2 failures
- **Workflow Efficiency**: 6-step build pipeline completes in ~743ms with optimal parallelization

## Architecture Components

### ResourceManager
- **Purpose**: Throttle concurrent operations to prevent resource exhaustion
- **Implementation**: SemaphoreSlim-based throttling
- **Features**: Metrics collection, dynamic concurrency adjustment

### ConcurrentExecutor
- **Purpose**: Execute multiple operations in parallel with error aggregation
- **Implementation**: Parallel.ForEachAsync with ConcurrentBag
- **Features**: Configurable parallelism, operation timeouts, progress reporting

### WorkflowEngine
- **Purpose**: Execute workflows with dependency management
- **Implementation**: Topological sorting with parallel step support
- **Features**: Dependency validation, context passing, progress tracking

### OrchestrationService
- **Purpose**: Coordinate all orchestration components
- **Implementation**: Tool registry with integrated resource management
- **Features**: Tool resolution, parallel execution, workflow coordination

## Code Structure

```
OrchestrationDemo/
├── Program.cs              # Main demo implementation
├── OrchestrationDemo.csproj # Project file
└── README.md               # This file
```

## Key Learnings

1. **Throttling Prevents Issues**: Without ResourceManager throttling, unlimited concurrent operations could exhaust system resources
2. **Error Handling Matters**: ContinueOnError allows workflows to complete partially rather than failing completely
3. **Dependencies Optimize**: WorkflowEngine automatically parallelizes independent steps while respecting dependencies
4. **Integration Value**: OrchestrationService provides a unified interface to all orchestration capabilities

## Next Steps

- See `DotNetDevMCP.Core.Tests/OrchestrationServiceTests.cs` for unit tests
- See `docs/architecture/orchestration-design.md` for detailed architecture
- Run performance benchmarks to measure exact speedup percentages
