---
title: Architecture
description: A map for contributors.
---
# Architecture

A map for contributors. The code is in [csa7mdm/DotNetDevMCP](https://github.com/csa7mdm/DotNetDevMCP).

## Projects

```mermaid
flowchart TB
    Server["DotNetDevMCP.Server<br/>CLI options, tool registration, stdio / HTTP"]
    CI["CodeIntelligence<br/>Roslyn workspace, SharpTool_* (fork of SharpTools)"]
    Testing["Testing<br/>dotnet test runner, TRX parsing, affected-test selection"]
    Build["Build<br/>dotnet build, restore, clean"]
    Analysis["Analysis<br/>dependencies, quality"]
    Orch["Orchestration<br/>ConcurrentExecutor, WorkflowEngine, ResourceManager"]
    SC["SourceControl<br/>git tools (opt-in)"]
    Mon["Monitoring<br/>process metrics (opt-in)"]
    Core["Core<br/>shared interfaces and models"]

    Server --> CI & Testing & Build & Analysis & Orch
    Server -. "--enable git" .-> SC
    Server -. "--enable monitoring" .-> Mon
    Testing --> CI
    CI & Testing & Orch --> Core
```
<!-- mermaid: rendered on GitHub; see the SVG diagrams for the Pages version -->


| Project | Start reading at |
|---|---|
| Server | `Program.cs`: options and `AddServices`, the one place every tool is registered |
| CodeIntelligence | `Services/SolutionManager.cs` (loading), `Mcp/Tools/AnalysisTools.cs` (find references), `Services/CodeModificationService.cs` (edits) |
| Testing | `TestRunner.cs` (VSTest and Microsoft.Testing.Platform), `AffectedTestFinder.cs` (the reference walk), `Mcp/Tools/TestingTools.cs` |
| Orchestration | `WorkflowEngine.cs`, `ConcurrentExecutor.cs`, `Mcp/Tools/OrchestrationTools.cs` |

## A tool call, end to end

```mermaid
sequenceDiagram
    participant Client as MCP client
    participant Server as DotNetDevMCP (stdio)
    participant Tool as tool method
    participant Ext as Roslyn / dotnet / git
    Client->>Server: tools/call {name, arguments} (JSON-RPC on stdin)
    Server->>Tool: bind arguments, inject services
    Tool->>Ext: in-process Roslyn call, or a child process
    Ext-->>Tool: result
    Tool-->>Server: object (serialized to JSON)
    Server-->>Client: result (stdout)
```
<!-- mermaid: rendered on GitHub; see the SVG diagrams for the Pages version -->


Two consequences of stdio that every contributor should know:

- **stdout is the protocol.** Logs go to stderr. Child processes get their own redirected stdout and a closed stdin;
  otherwise they read from the MCP connection and hang.
- **Tool parameters are arrays.** The MCP C# SDK tries to resolve `IEnumerable<T>` parameters from dependency injection;
  use `string[]`.

## Design decisions

Architecture decision records are in [docs/architecture/adr](https://github.com/csa7mdm/DotNetDevMCP/tree/main/docs/architecture/adr),
starting with why the Roslyn tools are a fork of [SharpTools](https://github.com/kooshi/SharpToolsMCP) rather than a
package reference.

How affected tests are chosen: [Affected Tests](affected-tests.md). Next: [Contributing](contributing.md).
