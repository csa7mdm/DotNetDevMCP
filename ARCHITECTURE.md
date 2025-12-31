# DotNetDevMCP - Architecture Documentation

**Version**: 0.1.0-alpha
**Last Updated**: December 31, 2025

---

## Table of Contents

1. [System Overview](#system-overview)
2. [Architectural Principles](#architectural-principles)
3. [Component Diagrams](#component-diagrams)
4. [Sequence Diagrams](#sequence-diagrams)
5. [Data Flow Diagrams](#data-flow-diagrams)
6. [Deployment Architecture](#deployment-architecture)
7. [Technology Stack](#technology-stack)
8. [Design Patterns](#design-patterns)

---

## System Overview

DotNetDevMCP is a layered, modular system designed to provide AI assistants with comprehensive .NET development capabilities through the Model Context Protocol (MCP).

### High-Level Architecture

```mermaid
graph TB
    subgraph "External Clients"
        AI[AI Assistants<br/>Claude, ChatGPT, etc.]
        IDE[IDE Extensions<br/>VS Code, Visual Studio]
        CLI[CLI Tools<br/>dotnet, custom tools]
    end

    subgraph "DotNetDevMCP System"
        subgraph "Presentation Layer"
            MCP_STDIO[MCP stdio Transport]
            MCP_SSE[MCP SSE Transport]
        end

        subgraph "Application Layer"
            TESTING[Testing Service]
            BUILD[Build Service]
            CODE_INT[Code Intelligence]
            SOURCE[Source Control]
        end

        subgraph "Domain Layer"
            ORCHESTRATION[Orchestration Engine]
            WORKFLOWS[Workflow Engine]
            RESOURCES[Resource Manager]
        end

        subgraph "Infrastructure Layer"
            DOTNET[.NET CLI Wrapper]
            ROSLYN[Roslyn Integration]
            GIT[Git Integration]
            FS[File System Access]
        end
    end

    subgraph "External Systems"
        RUNTIME[.NET Runtime]
        MSBUILD[MSBuild]
        TEST_RUNNERS[Test Runners<br/>xUnit, NUnit, MSTest]
        VCS[Version Control<br/>Git]
    end

    AI --> MCP_STDIO
    IDE --> MCP_SSE
    CLI --> MCP_STDIO

    MCP_STDIO --> TESTING
    MCP_STDIO --> BUILD
    MCP_STDIO --> CODE_INT
    MCP_SSE --> TESTING
    MCP_SSE --> BUILD

    TESTING --> ORCHESTRATION
    BUILD --> ORCHESTRATION
    CODE_INT --> ORCHESTRATION
    SOURCE --> ORCHESTRATION

    ORCHESTRATION --> WORKFLOWS
    ORCHESTRATION --> RESOURCES

    TESTING --> DOTNET
    BUILD --> DOTNET
    CODE_INT --> ROSLYN
    SOURCE --> GIT

    DOTNET --> RUNTIME
    DOTNET --> MSBUILD
    DOTNET --> TEST_RUNNERS
    ROSLYN --> RUNTIME
    GIT --> VCS
```

---

## Architectural Principles

### 1. Separation of Concerns

Each layer has a distinct responsibility:

- **Presentation Layer**: MCP protocol handling and transport
- **Application Layer**: Business logic and use cases
- **Domain Layer**: Core orchestration and coordination
- **Infrastructure Layer**: External system integration

### 2. Dependency Inversion

```mermaid
graph LR
    subgraph "High-Level Modules"
        TS[TestingService]
        BS[BuildService]
    end

    subgraph "Abstractions"
        ITE[ITestExecutor]
        IBE[IBuildExecutor]
    end

    subgraph "Low-Level Modules"
        DTE[DotNetTestExecutor]
        MSB[MSBuildExecutor]
    end

    TS -->|depends on| ITE
    BS -->|depends on| IBE
    DTE -->|implements| ITE
    MSB -->|implements| IBE

    style ITE fill:#4CAF50,stroke:#2E7D32,color:#fff
    style IBE fill:#4CAF50,stroke:#2E7D32,color:#fff
```

### 3. Single Responsibility Principle

Each class has one reason to change:

```mermaid
classDiagram
    class TestingService {
        +DiscoverTestsAsync()
        +RunTestsAsync()
        -Coordinates test operations
    }

    class DotNetTestDiscoveryService {
        +DiscoverAsync()
        -Discovers tests only
    }

    class DotNetTestExecutorService {
        +ExecuteAsync()
        -Executes tests only
    }

    class TestResultAggregator {
        +AddResult()
        +GetSummary()
        -Aggregates results only
    }

    TestingService --> DotNetTestDiscoveryService
    TestingService --> DotNetTestExecutorService
    TestingService --> TestResultAggregator
```

### 4. Composition Over Inheritance

Services are composed of smaller, focused components rather than deep inheritance hierarchies.

### 5. Async-First Design

All I/O operations are asynchronous with proper cancellation support.

---

## Component Diagrams

### Core Components

```mermaid
graph TB
    subgraph "DotNetDevMCP.Core"
        MODELS[Models<br/>Data Structures]
        INTERFACES[Interfaces<br/>Contracts]
        UTILITIES[Utilities<br/>Helpers]
    end

    subgraph "DotNetDevMCP.Orchestration"
        CE[ConcurrentExecutor<br/>Parallel Operations]
        RM[ResourceManager<br/>Throttling]
        WE[WorkflowExecutor<br/>Multi-Step]
        OS[OrchestrationService<br/>Unified API]
    end

    subgraph "DotNetDevMCP.Testing"
        TS[TestingService<br/>Coordination]
        TDS[DotNetTestDiscovery<br/>Test Discovery]
        TES[DotNetTestExecutor<br/>Test Execution]
        TRA[TestResultAggregator<br/>Results]
    end

    subgraph "DotNetDevMCP.Build"
        BS[BuildService<br/>Build Operations]
        DP[DiagnosticParser<br/>Error Parsing]
    end

    TS --> CE
    TS --> TDS
    TS --> TES
    TS --> TRA
    BS --> CE
    BS --> DP
    CE --> RM
    OS --> CE
    OS --> WE

    CE --> INTERFACES
    TS --> INTERFACES
    BS --> INTERFACES
    INTERFACES --> MODELS
```

### Testing Service Architecture

```mermaid
classDiagram
    class TestingService {
        -ITestDiscoveryService _discovery
        -ITestExecutorService _executor
        -IConcurrentExecutor _concurrent
        +DiscoverTestsAsync()
        +RunTestsAsync()
    }

    class ITestDiscoveryService {
        <<interface>>
        +DiscoverAsync()
    }

    class DotNetTestDiscoveryService {
        +DiscoverAsync()
        -ParseTestList()
    }

    class ITestExecutorService {
        <<interface>>
        +ExecuteAsync()
    }

    class DotNetTestExecutorService {
        +ExecuteAsync()
        -ParseTestResult()
    }

    class TestExecutionStrategy {
        <<enumeration>>
        Sequential
        FullParallel
        AssemblyLevelParallel
        SmartParallel
    }

    class TestResultAggregator {
        +AddResult()
        +GetSummary()
        -Calculate()
    }

    TestingService --> ITestDiscoveryService
    TestingService --> ITestExecutorService
    TestingService --> TestExecutionStrategy
    TestingService --> TestResultAggregator
    DotNetTestDiscoveryService ..|> ITestDiscoveryService
    DotNetTestExecutorService ..|> ITestExecutorService
```

### Orchestration Architecture

```mermaid
classDiagram
    class ConcurrentExecutor {
        +ExecuteAsync<T>(operations, options)
        -CreateTasks()
        -WaitForCompletion()
        -AggregateResults()
    }

    class ResourceManager {
        -SemaphoreSlim _semaphore
        +ExecuteWithThrottlingAsync<T>()
        +GetCurrentlyExecuting()
    }

    class WorkflowExecutor {
        +ExecuteAsync(workflow)
        -ExecuteStep()
        -HandleError()
    }

    class OrchestrationService {
        -ConcurrentExecutor _executor
        -ResourceManager _resources
        -WorkflowExecutor _workflow
        +ExecuteConcurrentlyAsync()
        +ExecuteWorkflowAsync()
    }

    class ConcurrentExecutionOptions {
        +MaxDegreeOfParallelism
        +ContinueOnError
        +OperationTimeout
    }

    class Workflow {
        +Name
        +Steps[]
    }

    OrchestrationService --> ConcurrentExecutor
    OrchestrationService --> ResourceManager
    OrchestrationService --> WorkflowExecutor
    ConcurrentExecutor --> ConcurrentExecutionOptions
    ConcurrentExecutor --> ResourceManager
    WorkflowExecutor --> Workflow
```

---

## Sequence Diagrams

### Test Execution Flow (Sequential)

```mermaid
sequenceDiagram
    participant Client
    participant TestingService
    participant Discovery
    participant Executor
    participant Aggregator

    Client->>TestingService: RunTestsAsync(Sequential)
    TestingService->>Discovery: DiscoverAsync()
    Discovery->>Discovery: Execute dotnet test --list-tests
    Discovery-->>TestingService: TestCase[]

    loop For each test
        TestingService->>Executor: ExecuteAsync(test)
        Executor->>Executor: Execute dotnet test --filter
        Executor->>Executor: Parse output
        Executor-->>TestingService: TestResult
        TestingService->>Aggregator: AddResult(result)
    end

    TestingService->>Aggregator: GetSummary()
    Aggregator-->>TestingService: TestRunSummary
    TestingService-->>Client: TestRunSummary
```

### Test Execution Flow (Parallel)

```mermaid
sequenceDiagram
    participant Client
    participant TestingService
    participant ConcurrentExecutor
    participant Executor
    participant Aggregator

    Client->>TestingService: RunTestsAsync(FullParallel)
    TestingService->>TestingService: DiscoverAsync()

    TestingService->>ConcurrentExecutor: ExecuteAsync(operations)

    par Test 1
        ConcurrentExecutor->>Executor: ExecuteAsync(test1)
        Executor-->>ConcurrentExecutor: result1
    and Test 2
        ConcurrentExecutor->>Executor: ExecuteAsync(test2)
        Executor-->>ConcurrentExecutor: result2
    and Test 3
        ConcurrentExecutor->>Executor: ExecuteAsync(test3)
        Executor-->>ConcurrentExecutor: result3
    and Test 4
        ConcurrentExecutor->>Executor: ExecuteAsync(test4)
        Executor-->>ConcurrentExecutor: result4
    end

    ConcurrentExecutor-->>TestingService: results[]

    loop For each result
        TestingService->>Aggregator: AddResult(result)
    end

    TestingService->>Aggregator: GetSummary()
    Aggregator-->>TestingService: TestRunSummary
    TestingService-->>Client: TestRunSummary (2.47x faster!)
```

### Build Process Flow

```mermaid
sequenceDiagram
    participant Client
    participant BuildService
    participant DotNetCLI
    participant DiagnosticParser

    Client->>BuildService: BuildAsync(solution, options)
    BuildService->>BuildService: ValidateOptions()
    BuildService->>DotNetCLI: Execute dotnet build
    DotNetCLI->>DotNetCLI: Compile
    DotNetCLI-->>BuildService: Output + Exit Code

    BuildService->>DiagnosticParser: Parse(output)
    DiagnosticParser->>DiagnosticParser: Extract errors
    DiagnosticParser->>DiagnosticParser: Extract warnings
    DiagnosticParser-->>BuildService: Diagnostic[]

    BuildService->>BuildService: Create BuildResult
    BuildService-->>Client: BuildResult
```

### Workflow Execution Flow

```mermaid
sequenceDiagram
    participant Client
    participant WorkflowExecutor
    participant Step1
    participant Step2
    participant Step3

    Client->>WorkflowExecutor: ExecuteAsync(workflow)

    WorkflowExecutor->>Step1: Execute("Restore")
    Step1->>Step1: dotnet restore
    Step1-->>WorkflowExecutor: "Packages restored"

    WorkflowExecutor->>Step2: Execute("Build")
    Step2->>Step2: dotnet build
    Step2-->>WorkflowExecutor: "Build successful"

    WorkflowExecutor->>Step3: Execute("Test")
    Step3->>Step3: dotnet test
    Step3-->>WorkflowExecutor: "42 tests passed"

    WorkflowExecutor-->>Client: WorkflowResult
```

---

## Data Flow Diagrams

### Test Discovery Data Flow

```mermaid
graph LR
    A[Test Assembly Path] --> B[DotNetTestDiscovery]
    B --> C[dotnet test --list-tests]
    C --> D[Raw Output]
    D --> E[Parse Test Names]
    E --> F[TestCase Objects]
    F --> G[Filter Optional]
    G --> H[TestCase Collection]

    style A fill:#e3f2fd
    style H fill:#c8e6c9
```

### Test Execution Data Flow

```mermaid
graph TB
    A[TestCase[]] --> B{Execution Strategy}

    B -->|Sequential| C[Execute One by One]
    B -->|FullParallel| D[Execute All Parallel]
    B -->|AssemblyParallel| E[Group by Assembly]
    B -->|SmartParallel| F[Group by Duration]

    C --> G[Execute Test]
    D --> H[ConcurrentExecutor]
    E --> I[Assembly Groups]
    F --> J[Duration Groups]

    H --> G
    I --> H
    J --> H

    G --> K[Parse Result]
    K --> L[TestResult]
    L --> M[TestResultAggregator]
    M --> N[TestRunSummary]

    style A fill:#e3f2fd
    style N fill:#c8e6c9
```

### Build Data Flow

```mermaid
graph TB
    A[Solution/Project Path] --> B[BuildService]
    B --> C[BuildOptions]
    C --> D[Generate CLI Arguments]
    D --> E[dotnet build with args]
    E --> F[MSBuild Process]
    F --> G[Build Output]

    G --> H[Success?]
    H -->|Yes| I[Extract Warnings]
    H -->|No| J[Extract Errors]

    I --> K[DiagnosticParser]
    J --> K

    K --> L[Parse Diagnostics]
    L --> M[Diagnostic Objects]
    M --> N[BuildResult]

    style A fill:#e3f2fd
    style N fill:#c8e6c9
```

---

## Deployment Architecture

### Local Development

```mermaid
graph TB
    subgraph "Developer Machine"
        DEV[Developer]
        IDE[IDE/Editor]
        CLI[Terminal/CLI]

        subgraph "DotNetDevMCP"
            MCP[MCP Server]
            SERVICES[Services]
        end

        subgraph ".NET SDK"
            DOTNET[dotnet CLI]
            RUNTIME[.NET Runtime]
        end
    end

    DEV --> IDE
    DEV --> CLI
    IDE --> MCP
    CLI --> MCP
    MCP --> SERVICES
    SERVICES --> DOTNET
    DOTNET --> RUNTIME

    style MCP fill:#4CAF50,stroke:#2E7D32,color:#fff
```

### CI/CD Pipeline

```mermaid
graph LR
    subgraph "GitHub Actions"
        TRIGGER[Push/PR]
        CHECKOUT[Checkout Code]
        RESTORE[dotnet restore]
        BUILD[dotnet build]
        TEST[DotNetDevMCP Test]
        PUBLISH[Publish Artifacts]
    end

    TRIGGER --> CHECKOUT
    CHECKOUT --> RESTORE
    RESTORE --> BUILD
    BUILD --> TEST
    TEST --> PUBLISH

    style TEST fill:#2196F3,stroke:#1565C0,color:#fff
```

### Production Deployment (Future)

```mermaid
graph TB
    subgraph "Client Layer"
        CLIENTS[AI Assistants]
    end

    subgraph "Load Balancer"
        LB[nginx/HAProxy]
    end

    subgraph "Application Layer"
        MCP1[MCP Server Instance 1]
        MCP2[MCP Server Instance 2]
        MCP3[MCP Server Instance N]
    end

    subgraph "Cache Layer"
        REDIS[Redis Cache]
    end

    subgraph "Storage"
        FS[File Storage]
        DB[Database]
    end

    CLIENTS --> LB
    LB --> MCP1
    LB --> MCP2
    LB --> MCP3

    MCP1 --> REDIS
    MCP2 --> REDIS
    MCP3 --> REDIS

    MCP1 --> FS
    MCP2 --> FS
    MCP3 --> FS

    MCP1 --> DB
    MCP2 --> DB
    MCP3 --> DB
```

---

## Technology Stack

### Runtime & Platform

```mermaid
graph TB
    subgraph "Platform"
        NET[.NET 9.0 Runtime]
        SDK[.NET 9.0 SDK]
        CSHARP[C# 13.0]
    end

    subgraph "Build Tools"
        MSBUILD[MSBuild 17.12+]
        NUGET[NuGet]
    end

    subgraph "Test Frameworks"
        XUNIT[xUnit 2.9.2]
        MSTEST[MSTest Future]
        NUNIT[NUnit Future]
    end

    SDK --> NET
    CSHARP --> SDK
    MSBUILD --> SDK
    NUGET --> SDK
    XUNIT --> NET
```

### Core Libraries

```mermaid
graph TB
    subgraph "Code Analysis"
        ROSLYN[Microsoft.CodeAnalysis 5.0+]
        DECOMPILER[ICSharpCode.Decompiler 9.1]
    end

    subgraph "Testing"
        TESTPLATFORM[Microsoft.TestPlatform 17.11+]
        TESTHOST[Microsoft.TestPlatform.TestHost]
    end

    subgraph "Future Libraries"
        MCP[ModelContextProtocol 0.4.0]
        LIBGIT[LibGit2Sharp 0.31]
    end

    subgraph "Cross-Cutting"
        DI[Microsoft.Extensions.DI]
        LOGGING[Microsoft.Extensions.Logging]
    end
```

---

## Design Patterns

### 1. Strategy Pattern (Test Execution)

```mermaid
classDiagram
    class TestExecutionStrategy {
        <<enumeration>>
        Sequential
        FullParallel
        AssemblyLevelParallel
        SmartParallel
    }

    class IExecutionStrategy {
        <<interface>>
        +Execute(tests)
    }

    class SequentialStrategy {
        +Execute(tests)
    }

    class ParallelStrategy {
        +Execute(tests)
    }

    class SmartParallelStrategy {
        +Execute(tests)
    }

    IExecutionStrategy <|.. SequentialStrategy
    IExecutionStrategy <|.. ParallelStrategy
    IExecutionStrategy <|.. SmartParallelStrategy
    TestExecutionStrategy --> IExecutionStrategy
```

### 2. Facade Pattern (OrchestrationService)

```mermaid
classDiagram
    class OrchestrationService {
        <<facade>>
        -ConcurrentExecutor
        -ResourceManager
        -WorkflowExecutor
        +ExecuteConcurrentlyAsync()
        +ExecuteWorkflowAsync()
        +ExecuteWithThrottlingAsync()
    }

    class Client {
        +DoWork()
    }

    Client --> OrchestrationService
    OrchestrationService --> ConcurrentExecutor
    OrchestrationService --> ResourceManager
    OrchestrationService --> WorkflowExecutor
```

### 3. Builder Pattern (Test/Build Options)

```mermaid
classDiagram
    class TestExecutionOptions {
        +Strategy
        +DefaultTimeout
        +MaxDegreeOfParallelism
        +ContinueOnError
    }

    class TestExecutionOptionsBuilder {
        +WithStrategy()
        +WithTimeout()
        +WithParallelism()
        +Build()
    }

    TestExecutionOptionsBuilder --> TestExecutionOptions : creates
```

### 4. Observer Pattern (Progress Reporting)

```mermaid
classDiagram
    class IProgress~T~ {
        <<interface>>
        +Report(value)
    }

    class ProgressReporter {
        +OnProgressChanged
        +Report(value)
    }

    class TestingService {
        +RunTestsAsync(progress)
        -ReportProgress()
    }

    IProgress <|.. ProgressReporter
    TestingService --> IProgress : uses
```

### 5. Repository Pattern (Future - Test Results)

```mermaid
classDiagram
    class ITestResultRepository {
        <<interface>>
        +SaveAsync(result)
        +GetByIdAsync(id)
        +GetAllAsync()
    }

    class TestResultRepository {
        +SaveAsync(result)
        +GetByIdAsync(id)
        +GetAllAsync()
    }

    class TestingService {
        -ITestResultRepository _repository
        +RunTestsAsync()
    }

    ITestResultRepository <|.. TestResultRepository
    TestingService --> ITestResultRepository
```

---

## Component Responsibilities

### DotNetDevMCP.Core
- **Purpose**: Foundation layer with shared abstractions
- **Responsibilities**:
  - Define core interfaces
  - Provide shared data models
  - Utility functions
- **Dependencies**: None (foundation layer)

### DotNetDevMCP.Orchestration
- **Purpose**: Concurrent execution and workflow management
- **Responsibilities**:
  - Parallel operation execution
  - Resource throttling
  - Multi-step workflow coordination
- **Dependencies**: Core

### DotNetDevMCP.Testing
- **Purpose**: Test discovery and execution
- **Responsibilities**:
  - Discover tests from assemblies
  - Execute tests with various strategies
  - Aggregate and report results
- **Dependencies**: Core, Orchestration

### DotNetDevMCP.Build
- **Purpose**: Build automation
- **Responsibilities**:
  - Build, clean, restore operations
  - Diagnostic parsing
  - Build result reporting
- **Dependencies**: Core, Orchestration

### DotNetDevMCP.CodeIntelligence
- **Purpose**: Code analysis and manipulation
- **Responsibilities**:
  - Symbol navigation
  - Find references
  - Code refactoring
- **Dependencies**: Core, Roslyn

### DotNetDevMCP.Server (Future)
- **Purpose**: MCP protocol implementation
- **Responsibilities**:
  - Protocol handling
  - Transport management (stdio/SSE)
  - Tool registration
- **Dependencies**: All services

---

## Performance Considerations

### Parallelization

```mermaid
graph LR
    A[Input Operations] --> B{Parallelizable?}
    B -->|Yes| C[ConcurrentExecutor]
    B -->|No| D[Sequential Execution]

    C --> E[Resource Manager]
    E --> F{Semaphore Available?}
    F -->|Yes| G[Execute]
    F -->|No| H[Wait]
    H --> F

    G --> I[Aggregate Results]
    D --> I

    style C fill:#4CAF50,stroke:#2E7D32,color:#fff
```

### Caching Strategy (Future)

```mermaid
graph TB
    REQUEST[Request] --> CACHE{In Cache?}
    CACHE -->|Yes| RETURN[Return Cached]
    CACHE -->|No| EXECUTE[Execute Operation]
    EXECUTE --> STORE[Store in Cache]
    STORE --> RETURN

    style CACHE fill:#FF9800,stroke:#E65100,color:#fff
```

---

## Security Architecture

### Trust Boundaries

```mermaid
graph TB
    subgraph "Untrusted Zone"
        USER[User Input]
        EXTERNAL[External Code]
    end

    subgraph "Trusted Zone"
        VALIDATOR[Input Validator]
        SANDBOX[Sandboxed Execution]
        SERVICES[DotNetDevMCP Services]
    end

    USER --> VALIDATOR
    EXTERNAL --> SANDBOX
    VALIDATOR --> SERVICES
    SANDBOX --> SERVICES

    style VALIDATOR fill:#F44336,stroke:#C62828,color:#fff
    style SANDBOX fill:#F44336,stroke:#C62828,color:#fff
```

---

## Extensibility Points

### Plugin Architecture (Future)

```mermaid
graph TB
    subgraph "Core System"
        PLUGIN_MGR[Plugin Manager]
        REGISTRY[Plugin Registry]
    end

    subgraph "Plugins"
        CUSTOM_TEST[Custom Test Provider]
        CUSTOM_BUILD[Custom Build Provider]
        CUSTOM_ANALYZER[Custom Analyzer]
    end

    PLUGIN_MGR --> REGISTRY
    CUSTOM_TEST --> REGISTRY
    CUSTOM_BUILD --> REGISTRY
    CUSTOM_ANALYZER --> REGISTRY

    style PLUGIN_MGR fill:#9C27B0,stroke:#6A1B9A,color:#fff
```

---

**Document Version**: 1.0
**Last Review**: December 31, 2025
**Next Review**: Quarterly

For more details, see:
- [PROJECT_SUMMARY.md](PROJECT_SUMMARY.md) - Comprehensive project overview
- [IMPLEMENTATION_SUMMARY.md](IMPLEMENTATION_SUMMARY.md) - Implementation details
- [docs/architecture/](docs/architecture/) - Detailed architecture docs
