---
title: Tools Reference
description: Generated from the server's own `tools/list` (0.
---
# Tools reference

Generated from the server's own `tools/list` (0.3.2). 37 tools are on by default; the git and monitoring groups are opt-in with `--enable git,monitoring` (53 tools).

Your agent sees each tool's full description and parameters; this page is the quick map.

## Code intelligence (Roslyn) (21)

| Tool | What it does | Parameters |
|---|---|---|
| `SharpTool_AddMember` | Adds one or more new member definitions (Property, Field, Method, inner Class, etc.) to a specified type. | `fullyQualifiedTargetName`, `codeSnippet`, `fileNameHint`, `lineNumberHint`, `commitMessage` |
| `SharpTool_AnalyzeComplexity` | Deep analysis of code complexity metrics including cyclomatic complexity, cognitive complexity, method stats, coupling, and inheritance depth. | `scope`, `target` |
| `SharpTool_CreateRoslynDocument` | Creates a new document file with the specified content. | `filePath`, `content`, `commitMessage` |
| `SharpTool_FindAndReplace` | Regex find-and-replace in a file, type or glob of files, with a compile check of the result. | `regexPattern`, `replacementText`, `target`, `commitMessage` |
| `SharpTool_FindReferences` | Finds all references to a specified symbol with surrounding context. | `fullyQualifiedSymbolName` |
| `SharpTool_GetMembers` | Lists the full signatures of members of a specified type, including XML documentation. | `fullyQualifiedTypeName`, `includePrivateMembers` |
| `SharpTool_ListImplementations` | Gets the locations and FQNs of all implementations of an interface or abstract method, and lists derived classes for a base class. | `fullyQualifiedSymbolName` |
| `SharpTool_LoadProject` | Type tree of one project (every type and member signature) without reading files. Call after LoadSolution. | `projectName` |
| `SharpTool_LoadSolution` | Loads a `.sln` or `.slnx` into Roslyn. Call this first (or start the server with `--load-solution`). | `solutionPath` |
| `SharpTool_ManageAttributes` | Reads or writes all attributes on a declaration. | `operation`, `codeToWrite`, `targetDeclaration` |
| `SharpTool_ManageUsings` | Reads or writes using directives in a document. | `operation`, `codeToWrite`, `filePath` |
| `SharpTool_MoveMember` | Moves a member (property, field, method, nested type, etc.) from one type/namespace to another. | `fullyQualifiedMemberName`, `fullyQualifiedDestinationTypeOrNamespaceName`, `commitMessage` |
| `SharpTool_OverwriteMember` | Replaces the definition of an existing member or type with new C# code, or deletes it. | `fullyQualifiedMemberName`, `newMemberCode`, `commitMessage` |
| `SharpTool_OverwriteRoslynDocument` | Overwrites an existing document file with the specified content. | `filePath`, `content`, `commitMessage` |
| `SharpTool_ReadRawFromRoslynDocument` | Reads the content of a file in the solution or referenced directories. | `filePath` |
| `SharpTool_ReadTypesFromRoslynDocument` | Returns a comprehensive tree of types (classes, interfaces, structs, etc.) and their members from a specified file. | `filePath` |
| `SharpTool_RenameSymbol` | Renames a symbol (variable, method, property, type) and updates all references. | `fullyQualifiedSymbolName`, `newName`, `commitMessage` |
| `SharpTool_RequestNewTool` | Allows requesting a new tool to be added to the SharpTools MCP server. | `toolName`, `toolDescription`, `expectedParameters`, `expectedOutput`, `justification` |
| `SharpTool_SearchDefinitions` | Dual-engine pattern search across source code AND compiled assemblies for public APIs. | `regexPattern` |
| `SharpTool_Undo` | Reverts the last applied change. Needs `--git-commit-edits`. | - |
| `SharpTool_ViewDefinition` | Displays the verbatim source code from the declaration of a target symbol (class, method, property, etc.) with indentation omitted to save tokens. | `fullyQualifiedSymbolName` |

## Testing (3)

| Tool | What it does | Parameters |
|---|---|---|
| `dotnet_test_affected` | Finds the tests that reference the code in the changed files (via Roslyn, through the loaded solution) and runs only those. | `changedFiles`, `gitBase`, `maxDepth`, `dryRun`, `noBuild`, `maxSelectionSeconds`, `framework`, `maxSelectedFraction`, `timeoutSeconds` |
| `dotnet_test_discover` | Lists the tests in a test project (dotnet test --list-tests). | `projectPath`, `filter` |
| `dotnet_test_run` | Runs tests in a project or a whole solution with one dotnet test invocation and returns per-test results, failures with messages and stack traces. | `path`, `filter`, `testNames`, `noBuild`, `framework`, `timeoutSeconds` |

## Build (4)

| Tool | What it does | Parameters |
|---|---|---|
| `dotnet_build` | Builds a .NET project or solution with configurable options. | `projectPath`, `configuration`, `framework`, `runtime`, `verbosity`, `noRestore`, `verbose` |
| `dotnet_build_with_properties` | Builds a .NET project with custom MSBuild properties. | `projectPath`, `properties`, `configuration`, `framework`, `verbose` |
| `dotnet_clean` | Cleans build artifacts from a .NET project or solution. | `projectPath`, `configuration`, `verbose` |
| `dotnet_restore` | Restores NuGet packages for a .NET project or solution. | `projectPath`, `verbose` |

## Analysis (5)

| Tool | What it does | Parameters |
|---|---|---|
| `dotnet_analyze_project` | Project facts: target frameworks, references, metrics and dependencies. | `path`, `includeMetrics`, `includeDependencies` |
| `dotnet_analyze_quality` | Code-quality metrics for a project or folder. | `path` |
| `dotnet_detect_circular_dependencies` | Project reference cycles in a solution. | `solutionPath` |
| `dotnet_get_dependencies` | NuGet and project dependencies of a project. | `projectPath` |
| `dotnet_scan_outdated_packages` | NuGet packages with newer versions available. | `projectPath` |

## Orchestration (4)

| Tool | What it does | Parameters |
|---|---|---|
| `configure_resource_limits` | Sets the maximum number of orchestrated operations that may run concurrently. | `maxConcurrency` |
| `execute_workflow` | Runs tools of this server as a dependency graph: steps whose dependencies are done run in parallel, dependents wait. | `workflowName`, `steps` |
| `get_resource_metrics` | Returns current concurrency limits and how many orchestrated operations are running or queued. | - |
| `orchestrate_parallel` | Runs several tools of this server concurrently (throttled by the resource manager) and returns every result. | `operations`, `maxParallelism` |

## Git (opt-in: --enable git) (10)

| Tool | What it does | Parameters |
|---|---|---|
| `git_checkout_branch` | Switches to an existing git branch. | `repoPath`, `branchName` |
| `git_commit` | Commits staged changes with a message. | `repoPath`, `message`, `allowEmpty` |
| `git_create_branch` | Creates a new git branch and switches to it. | `repoPath`, `branchName`, `startPoint` |
| `git_diff` | Shows differences between commits, branches, or working directory changes. | `repoPath`, `file`, `commit1`, `commit2` |
| `git_list_branches` | Lists all branches in a git repository, with option to include remote branches. | `repoPath`, `includeRemote` |
| `git_log` | Gets the commit history/log for a repository or branch. | `repoPath`, `count`, `branch` |
| `git_pull` | Pulls changes from a remote repository and merges them into the current branch. | `repoPath`, `remote`, `branch` |
| `git_push` | Pushes committed changes to a remote repository. | `repoPath`, `remote`, `branch`, `force` |
| `git_repo_status` | Gets comprehensive information about a git repository including current branch, changes, ahead/behind status, and remotes. | `repoPath` |
| `git_stage_changes` | Stages file changes for commit. | `repoPath`, `files` |

## Monitoring (opt-in: --enable monitoring) (6)

| Tool | What it does | Parameters |
|---|---|---|
| `dotnet_check_health` | Health check of the server process. | - |
| `dotnet_force_gc` | Force a garbage collection (diagnostics only). | `generation`, `blocking` |
| `dotnet_get_gc_stats` | Garbage-collector statistics. | - |
| `dotnet_get_performance_metrics` | CPU, memory and thread metrics of the server process. | - |
| `dotnet_get_resource_utilization` | Current resource use of the server process. | - |
| `dotnet_start_profiling_session` | Collect CPU, memory and GC samples for a number of seconds. | `sessionName`, `durationSeconds`, `collectCpuSamples`, `collectMemorySamples`, `collectGcEvents` |

See [Affected Tests](affected-tests.md) for how `dotnet_test_affected` decides, and [Configuration](configuration.md) for server options.
