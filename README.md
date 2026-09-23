# DotNetDevMCP

<!-- mcp-name: io.github.csa7mdm/dotnetdevmcp -->

An [MCP](https://modelcontextprotocol.io) server that gives AI coding agents real .NET tooling: Roslyn code intelligence, `dotnet build`/`test`, git, and an orchestrator that runs those tools concurrently as a dependency graph.

[![Build and Test](https://github.com/csa7mdm/DotNetDevMCP/actions/workflows/build.yml/badge.svg)](https://github.com/csa7mdm/DotNetDevMCP/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/DotNetDevMCP.svg)](https://www.nuget.org/packages/DotNetDevMCP)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

Agents working on .NET code usually get by with `grep` and shelling out to `dotnet`. That means they read files instead of symbols, edit text instead of syntax trees, and run one command at a time. DotNetDevMCP replaces that with 53 tools that use the compiler's view of your solution and can run builds, tests and analysis in parallel.

## Install

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

**Claude Code**

```bash
claude mcp add dotnetdevmcp -- dnx DotNetDevMCP --yes
```

**VS Code / Visual Studio** (`.mcp.json` or `.vscode/mcp.json`)

```json
{
  "servers": {
    "dotnetdevmcp": {
      "type": "stdio",
      "command": "dnx",
      "args": ["DotNetDevMCP", "--yes"]
    }
  }
}
```

**Claude Desktop / Cursor / any stdio client** (`mcpServers` form)

```json
{
  "mcpServers": {
    "dotnetdevmcp": {
      "command": "dnx",
      "args": ["DotNetDevMCP", "--yes", "--", "--load-solution", "C:/src/MyApp/MyApp.sln"]
    }
  }
}
```

`dnx` downloads the package from NuGet.org on first run. Prefer a permanent install? `dotnet tool install -g DotNetDevMCP`, then use `dotnetdevmcp` as the command.

Pass `--load-solution <path>` to have Roslyn load your solution at startup, or let the agent call `SharpTool_LoadSolution` when it needs to. `--http --port 3001` serves Streamable HTTP instead of stdio. `dotnetdevmcp --help` lists everything.

By default, the Roslyn edit tools (`SharpTool_RenameSymbol`, `OverwriteMember`, `AddMember`, `MoveMember`, `FindAndReplace`, `CreateRoslynDocument`, `OverwriteRoslynDocument`, `ManageUsings`, `ManageAttributes`) never touch git - they apply changes to disk and return the usual compile-check output, nothing else. Pass `--git-commit-edits` to opt into the old behavior: each edit creates a `sharptools/<timestamp>` branch (if you aren't already on one) and commits the change, which is also what `SharpTool_Undo` needs in order to revert. Without the flag, `SharpTool_Undo` returns an explanatory error instead of failing obscurely. (`--disable-git` still exists but is a no-op now that git integration is opt-in by default.)

## What the agent gets

| Group | Tools | What they do |
|---|---|---|
| Code intelligence (Roslyn) | 21 | Load a solution; search and view definitions; find references and implementations; add, overwrite, move and rename members; manage usings and attributes; find-and-replace with syntax awareness; complexity analysis; undo. Forked from [SharpTools](https://github.com/kooshi/SharpToolsMCP). |
| Testing | 3 | `dotnet_test_run` (one `dotnet test` per project or solution, TRX parsed into per-test results with messages and stack traces), `dotnet_test_discover`, and `dotnet_test_affected`: Roslyn walks references from your changed files to the test methods that reach them, and runs only those. |
| Build | 5 | `dotnet build`, `restore`, `clean`, build with MSBuild properties, scan for outdated packages. Structured error/warning output. |
| Analysis | 6 | Project dependency graph, circular-dependency detection, quality metrics, health check. |
| Git | 10 | Status, branches, checkout, stage, commit, diff, log, push, pull. |
| Orchestration | 4 | `orchestrate_parallel` runs any of the server's own tools concurrently; `execute_workflow` runs them as a DAG. Resource limits and metrics. |
| Monitoring | 4 | Process performance metrics, GC stats, resource utilization, profiling sessions. |

Things you can say to an agent with this server attached:

- "Load `MyApp.sln`, find every implementation of `IOrderRepository`, and rename `GetById` to `FindById` across the solution."
- "Run only the tests affected by what I just changed, and show me the failures with stack traces."
- "Build the API and the worker projects at the same time, then run both test projects."
- "Which projects have circular dependencies?"

## Orchestration

The tools above are individually useful. The orchestrator is what makes them fast. Any tool on the server can be dispatched by name, in parallel or as a dependency graph, from a single call:

```json
{
  "name": "execute_workflow",
  "arguments": {
    "workflowName": "ci",
    "steps": [
      { "name": "build-tests",  "toolName": "dotnet_build",    "arguments": { "projectPath": "tests/Api.Tests/Api.Tests.csproj" } },
      { "name": "build-worker", "toolName": "dotnet_build",    "arguments": { "projectPath": "src/Worker/Worker.csproj" } },
      { "name": "test-api",     "toolName": "dotnet_test_run", "arguments": { "path": "tests/Api.Tests/Api.Tests.csproj", "noBuild": true }, "dependsOn": ["build-tests"] },
      { "name": "deps",         "toolName": "dotnet_detect_circular_dependencies", "arguments": { "projectPath": "src/Api/Api.csproj" } }
    ]
  }
}
```

`build-tests`, `build-worker` and `deps` start immediately; `test-api` waits for `build-tests`. Steps are throttled by a resource manager (default: processor count, adjustable with `configure_resource_limits`). Failures are reported per step; a failed dependency stops its dependents.

Under the hood this is `ConcurrentExecutor` / `WorkflowEngine` / `ResourceManager`, plain C# classes in `DotNetDevMCP.Orchestration` that can be used without MCP.

## Numbers

Measured with BenchmarkDotNet on an i7-10750H, .NET 10.0.9. The orchestration benchmarks use `Task.Delay` stand-ins for I/O-bound work, so they measure the engine's overhead and scheduling, not `dotnet` itself.

| Scenario (20 ops × 50 ms) | Mean | vs sequential |
|---|---:|---:|
| Sequential | 1,237 ms | 1.00 |
| `ConcurrentExecutor`, throttled to 5 | 246 ms | 0.20 |
| `ConcurrentExecutor`, unthrottled (12 cores) | 123 ms | 0.10 |
| `WorkflowEngine` with dependencies | 185 ms | 0.15 |
| `Task.WhenAll` (lower bound) | 62 ms | 0.05 |

| Workflow with a mix of dependent and independent steps | Mean | vs sequential |
|---|---:|---:|
| Sequential | 308 ms | 1.00 |
| `WorkflowEngine` | 185 ms | 0.60 |

## Affected tests

After an edit, the agent usually reruns the whole suite. `dotnet_test_affected` asks Roslyn instead: take the symbols declared in the changed files, follow references (up to `maxDepth` hops, default 3) until you land in a method with `[Fact]`, `[Theory]`, `[Test]`, `[TestCase]` or `[TestMethod]`, then run exactly those with `dotnet test --filter`. Changed files default to the git working tree, or `gitBase: "main"` for a branch. `dryRun: true` lists the tests without running them.

On this repository, editing `ConcurrentExecutor.cs` selects 22 of 44 tests (the `ConcurrentExecutorTests` plus the `OrchestrationServiceTests` that reach it through `OrchestrationService`). Measured through the MCP tool, build included, i7-10750H:

| | Tests | Wall |
|---|---:|---:|
| `dotnet test` from a shell | 44 | 9 s |
| `dotnet_test_run` | 44 | 8.3 s |
| `dotnet_test_affected` (change to `ConcurrentExecutor.cs`) | 22 | 6.6 s |

The suite here is small, so the saving is small; the selection scales with the ratio of touched code to suite size, not with machine cores. Selection itself (the Roslyn reference walk) takes 4-6 s on this solution; `dryRun: true` shows what it picked and why (`via`). Reproduce the engine benchmarks with `dotnet run -c Release --project benchmarks/DotNetDevMCP.Benchmarks`.

## Build from source

```bash
git clone https://github.com/csa7mdm/DotNetDevMCP.git
cd DotNetDevMCP
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/DotNetDevMCP.Server -- --help
```

To use a local build from an MCP client, point `command` at `src/DotNetDevMCP.Server/bin/Release/net10.0/dotnetdevmcp` (`.exe` on Windows).

## Layout

```
src/
  DotNetDevMCP.Server/           entry point; stdio or HTTP; packs as the `dotnetdevmcp` tool
  DotNetDevMCP.CodeIntelligence/ Roslyn tools (SharpTools fork)
  DotNetDevMCP.Testing/          dotnet test runner, TRX parsing, Roslyn affected-test selection
  DotNetDevMCP.Build/            dotnet build/restore/clean
  DotNetDevMCP.Analysis/         dependency graph, quality metrics
  DotNetDevMCP.SourceControl/    git
  DotNetDevMCP.Orchestration/    ConcurrentExecutor, WorkflowEngine, ResourceManager, orchestration tools
  DotNetDevMCP.Monitoring/       process metrics
  DotNetDevMCP.Core/             interfaces and models shared by the above
tests/                           xUnit tests for the orchestration core and the TRX parser
benchmarks/                      BenchmarkDotNet suite
docs/architecture/               design notes and ADRs
```

Built on the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) 2.x. Package versions are managed centrally in `Directory.Packages.props`.

## Status

0.1.0. The Roslyn tools are mature (they come from SharpTools). Testing, build, git and orchestration are newer and have been exercised on this repository and a few others; expect rough edges on unusual project layouts. Issues and PRs welcome, see [CONTRIBUTING.md](CONTRIBUTING.md).

Known gaps: `dotnet_test_affected` follows C# references only (no reflection, no DI-by-convention, no string-keyed lookups), so a change reached only through those paths will not select the test; use `maxDepth` and `dryRun` to check what it picks. Test attribute detection covers xUnit, NUnit and MSTest by attribute name. Passing more than a few hundred exact test names to `dotnet test --filter` will exceed the command-line limit; run the project instead.

## Credits and license

MIT. The code-intelligence module is a fork of [SharpTools](https://github.com/kooshi/SharpToolsMCP) by кɵɵѕнī, also MIT; see [LICENSE](LICENSE) and [ADR-001](docs/architecture/adr/001-fork-sharptools.md) for why it was forked rather than referenced.
