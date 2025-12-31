# DotNetDevMCP Architecture

**Last Updated**: December 31, 2025

## 📋 Table of Contents

- [Overview](#overview)
- [System Architecture](#system-architecture)
- [Core Components](#core-components)  
- [Data Flow](#data-flow)
- [Technology Stack](#technology-stack)
- [Design Principles](#design-principles)

---

## Overview

DotNetDevMCP is a Model Context Protocol (MCP) server designed to provide AI assistants with professional-grade .NET development capabilities. The architecture emphasizes **concurrent execution**, **intelligent orchestration**, and **production-ready integration** with the .NET SDK.

### Key Architectural Decisions

1. **Concurrency-First Design**: All operations support parallel execution by default
2. **Separation of Concerns**: Clear boundaries between core, orchestration, and domain services
3. **Real SDK Integration**: Direct use of `dotnet` CLI rather than abstractions
4. **Cancellation Support**: Proper CancellationToken propagation throughout
5. **Progress Reporting**: IProgress<T> for real-time operation updates

---

## System Architecture

The system follows a layered architecture with clear separation of concerns:

- **MCP Server Layer**: stdio and SSE/HTTP transports
- **Service Layer**: Testing, Build, Source Control, Code Intelligence
- **Orchestration Layer**: Concurrent execution, resource management, workflows
- **Core Infrastructure**: Models, interfaces, shared utilities
- **External Dependencies**: .NET CLI, Roslyn, LibGit2Sharp, ASP.NET Core

---

## Core Components

### Orchestration Layer

#### ConcurrentExecutor
Executes multiple operations in parallel with intelligent resource management.

**Features**: Configurable parallelism, continue-on-error, timeout handling, progress reporting, error aggregation

**Performance**: 2-3x faster than sequential execution

#### ResourceManager
Prevents system overload with smart throttling using semaphore-based concurrency control.

**Features**: Dynamic max concurrency, operation tracking, resource metrics

#### WorkflowEngine
Multi-step workflows with dependency management and parallel execution support.

**Features**: Dependency graph validation, error handling, full cancellation support

**Recent Fixes**: Proper OperationCanceledException propagation

### Service Layer

#### TestingService
Intelligent test orchestration with 4 execution strategies (Sequential, FullParallel, AssemblyLevelParallel, SmartParallel).

**Performance**: 2.47x speedup in parallel mode

#### BuildService
Professional build automation with diagnostic parsing for errors and warnings.

#### GitService  
Advanced Git integration with merge analysis and code review capabilities.

**New Features**:
- Merge analysis with conflict detection
- Code review statistics (files changed, lines added/removed)
- Powered by LibGit2Sharp

### MCP Server Layer

#### Stdio Transport
Standard input/output communication for CLI integration with structured logging.

#### SSE/HTTP Transport
Server-Sent Events over HTTP for web-based integration using ASP.NET Core.

---

## Technology Stack

- **.NET 9.0**
- **C# 13** with records and nullable references
- **Roslyn** for code analysis
- **LibGit2Sharp** for Git operations
- **xUnit 2.9.2** + **FluentAssertions 8.8.0**
- **Serilog** for structured logging
- **ASP.NET Core** for HTTP transport

---

## Design Principles

1. **Concurrency by Default**: Parallel execution out of the box
2. **Proper Cancellation Support**: Every async operation accepts CancellationToken
3. **Progress Reporting**: IProgress<T> for real-time updates
4. **Resource Management**: Semaphore-based throttling to prevent overload
5. **Error Handling**: Comprehensive error aggregation with structured information
6. **Real SDK Integration**: Direct use of dotnet CLI and Git
7. **Token Efficiency**: Designed for AI assistants with ~10% token savings

---

## Recent Improvements (December 31, 2025)

### Cancellation Support Fixes

**Problem**: Tests timing out after 5 minutes in CI/CD

**Solutions**:
1. Removed fire-and-forget Task.Run in ResourceManager
2. Added explicit OperationCanceledException re-throw in WorkflowEngine and ConcurrentExecutor

**Impact**: Tests complete in ~2 seconds instead of timing out

### Git Enhancements

**New Features**:
- AnalyzeMergeAsync: Detects merge conflicts before merging
- ReviewChangesAsync: Provides detailed code review statistics

---

## Performance Characteristics

- **Testing**: 2.47x speedup in parallel mode
- **Orchestration**: 2-3x faster on typical workloads
- **Resource overhead**: <1%

---

## License

This project is licensed under the MIT License - see LICENSE for details.
