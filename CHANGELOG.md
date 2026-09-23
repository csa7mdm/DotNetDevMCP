# Changelog

All notable changes to DotNetDevMCP are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

## [0.3.0] - 2026-09-23

Found by running the tools on [Polly](https://github.com/App-vNext/Polly); method and numbers in [benchmarks/polly](benchmarks/polly/README.md).

### Added
- Microsoft.Testing.Platform support: when global.json sets `"test": { "runner": "Microsoft.Testing.Platform" }`, `dotnet_test_run` and `dotnet_test_affected` use `--project`/`--solution`, xUnit v3's `--report-xunit-trx`/`--filter-method` (MSTest/NUnit: `--report-trx`/`--filter`), and read the TRX paths `dotnet test` reports instead of forcing `--results-directory`, which repos often set themselves. Before, every run on such a repo failed.
- `framework` option on `dotnet_test_run` and `dotnet_test_affected`: run one target framework of multi-targeted test projects. On Polly a one-file change ran in 4.6 s instead of 16.8 s.
- `dotnet_test_affected` reports `selectionComplete` and `symbolsSearched`, and has a time budget (`maxSelectionSeconds`, default 10). Out of budget, it runs the whole solution and says so instead of returning a partial selection.

### Changed
- `dotnet_test_affected` default `maxDepth` is 8 (was 3): at 3 the selections missed tests reached through overload chains. With the fixes below they included 111 of the 112 tests that injected faults broke in Polly, up from 101.
- Affected test projects build one at a time, then run in parallel; parallel builds of projects sharing references collided on file locks.
- Git (`git_repo_status`, `git_list_branches`, ..., 10 tools) and Monitoring (`dotnet_get_performance_metrics`, ..., 6 tools) are now opt-in via `--enable git,monitoring`, off by default: they add nothing over the shell an agent already has, and every registered tool costs context tokens in every session. The server now exposes 37 tools by default instead of 53.

### Fixed
- Affected-test selection never finished on multi-targeted solutions (over 70 minutes on Polly for one commit): every TFM of every project was searched, the same symbols were walked once per TFM, and hops went through whole types. It now searches one TFM variant of each test project with its references, recognizes a symbol across TFMs, and hops through constructors instead of types.
- Tests fed by xUnit `[MemberData]` from a static field were not selected: the walk went from the field initializer to the static constructor, which nothing references. It now follows the field too.
- `dotnet` ran in the server's working directory, so the repository's global.json (pinned SDK, test runner mode) was ignored. Test, build, clean and restore now start in the project's directory.
- `SharpTool_FindReferences` counted each reference once per target framework (925 for a property with 79 matching lines on Polly) and could show the same location repeatedly. References are now unique by file and position.

## [0.2.2] - 2026-09-23

### Fixed
- README carries the `mcp-name: io.github.csa7mdm/dotnetdevmcp` marker the MCP Registry needs to verify ownership of the NuGet package. Build and Release fail if the packed README lacks it.

## [0.2.1] - 2026-09-23

### Changed
- `dotnet_build` and `dotnet_build_with_properties` return a compact summary by default: counts, every error, and up to 20 de-duplicated warnings with paths relative to the project. A clean build of this repo went from 55,960 to 8,618 characters. `verbose: true` adds the raw MSBuild lines. `dotnet_clean`/`dotnet_restore` return no output on success and a 20-line tail on failure.
- Edit tools (`SharpTool_RenameSymbol`, `OverwriteMember`, `AddMember`, `MoveMember`, `FindAndReplace`, `CreateRoslynDocument`, `OverwriteRoslynDocument`, `ManageUsings`, `ManageAttributes`) no longer create a `sharptools/<timestamp>` branch and commit by default. Opt in with `--git-commit-edits`, which `SharpTool_Undo` requires. `--disable-git` is now a no-op.
- Descriptions no longer mention parallel test execution.

### Fixed
- The package stored `server.json` as `.mcp//server.json` when packed on Linux, so nuget.org showed no MCP server configuration.
- Edit tools formatted every changed file in full, so untouched code that didn't match `.editorconfig` was rewritten too: renaming one method in this repo produced a 95-line diff. They now format only the text they changed (the same rename is a 2-line diff).
- `SharpTool_AnalyzeComplexity` emitted empty `{}` entries for constructors, operators and accessors; every entry now carries `name`.
- `dotnet build`/`clean`/`restore` child processes inherited the MCP stdin pipe.

## [0.2.0] - 2026-09-23

### Changed
- `dotnet_test_run` now runs one `dotnet test` per project or solution and parses the TRX it writes (per-test outcome, duration, message, stack trace, stdout). The four execution strategies and the process-per-test runner are gone: plain `dotnet test` on this repo's 44 tests takes 9 s, the old "SmartParallel" runner took 22 s. `strategy`, `maxParallelTests`, `timeoutSeconds` and `continueOnFailure` parameters removed; `assemblyPath` renamed to `path`; `filter` and `noBuild` added.
- `dotnet_test_run_solution` removed; pass a `.sln` to `dotnet_test_run`.
- Testing project references CodeIntelligence (for the Roslyn workspace) and no longer depends on `xunit.runner.utility` or `Microsoft.TestPlatform.ObjectModel`.

### Added
- `dotnet_test_affected`: walks Roslyn references from the symbols declared in changed files to the test methods that reach them, and runs only those. Changed files default to the git working tree (`gitBase` for a branch diff). `dryRun` lists without running.
- Unit tests for the TRX parser.

### Fixed
- Child processes (`dotnet`, `git`) inherited the server's stdin, which in stdio mode is the MCP pipe; `git status` blocked on it for about two minutes per call. All three std handles are now redirected and stdin is closed.

### Removed
- `TestingServiceDemo` and `RealTestExecutionDemo` samples (they demonstrated the removed strategies).

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
