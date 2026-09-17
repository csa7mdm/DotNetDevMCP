# Changelog

All notable changes to DotNetDevMCP are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

## [0.1.0] - 2026-09-17

First release that builds, tests and runs end to end. Earlier commits on `main` were never released.

### Added
- Single `dotnetdevmcp` entry point (`src/DotNetDevMCP.Server`), stdio by default, `--http --port N` for Streamable HTTP. Packs as a .NET tool with the `McpServer` package type and an embedded `.mcp/server.json`, so it can be run with `dnx DotNetDevMCP` or installed with `dotnet tool install -g DotNetDevMCP`.
- All 53 tools are registered on that one server: Roslyn code intelligence (21), testing (3), build (5), analysis (6), git (10), orchestration (4), monitoring (4). Previously only the Roslyn tools were wired up.
- `orchestrate_parallel` and `execute_workflow` now dispatch to the server's own tool collection through the MCP SDK (`McpServerTool.InvokeAsync`), so any tool can be run in parallel or as a dependency graph from one call.
- `Directory.Packages.props` (central package management) so Dependabot can no longer drift package versions per project.
- CI that fails when the build or tests fail, runs on Ubuntu and Windows, smoke-tests the stdio handshake and `tools/list`, and packs the tool. Release workflow publishes to NuGet.org and creates a GitHub release on `v*` tags.
- BenchmarkDotNet results and a real `dotnet_test_run` timing (44 tests: 83.8 s sequential, 22.0 s SmartParallel) in the README.

### Changed
- Migrated from `ModelContextProtocol` 0.1/0.4 preview to 2.2.0.
- `FluentAssertions` 8.x (commercial license) replaced by `AwesomeAssertions`.
- Merged `DotNetDevMCP.Server.Stdio` and `DotNetDevMCP.Server.Sse` into `DotNetDevMCP.Server`; removed the empty `DotNetDevMCP.Documentation` project.
- README rewritten around install, tool list, orchestration and measured numbers. Removed the unverified "50-80% faster" badge and the overlapping status documents.

### Fixed
- Build: Dependabot had pinned `xunit.runner.utility`/`xunit.extensibility.execution` 3.0.0, `NuGet.Protocol` 8.0.0, `Serilog.Sinks.Async` 3.0.0 and `Serilog.Sinks.File` 8.0.0, none of which exist.
- Build: the Build, Testing, Orchestration, SourceControl, Analysis and Monitoring projects had never compiled (missing usings, a `WorkflowStep` type that did not exist, wrong `ToolResult` member names, `GitService` using the wrong variable).
- `ConcurrentExecutor`: a per-operation timeout was treated as caller cancellation and aborted the whole batch; it is now recorded as a `TimeoutException` for that operation.
- `ResourceManager`: changing `MaxConcurrency` disposed the semaphore that in-flight operations still held. Operations now capture and release their own semaphore instance.
- Tool parameters declared as `IEnumerable<T>` (`orchestrate_parallel`, `execute_workflow`, `dotnet_test_run.testNames`, `git_stage_changes.files`) were bound from DI as empty and hidden from the JSON schema; they are arrays now.
- Tests: a lazy `Select` that never started its tasks (hung the test host), a `SemaphoreSlim(0, 1)` released three times, and progress assertions that raced `Progress<T>`'s async callbacks.
