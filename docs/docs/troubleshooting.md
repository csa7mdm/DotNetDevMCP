---
title: "Troubleshooting"
description: "Start the server with `--log-level Debug --log-directory <dir>` to get logs for any of these."
---
# Troubleshooting

Start the server with `--log-level Debug --log-directory <dir>` to get logs for any of these. Didn't find your problem?
[Open a bug report](https://github.com/csa7mdm/DotNetDevMCP/issues/new?template=bug_report.yml) and add it here once
it's solved.

## The server doesn't start, or the client shows no tools

- **`dotnet --version` must be 10.x.** DotNetDevMCP needs the .NET 10 SDK (your solution can target older frameworks).
- **`'dnx' is not recognized` or the client fails silently on Windows.** Use `"command": "dotnet"` with
  `"args": ["dnx", "DotNetDevMCP", "--yes"]`. Some clients start servers without a shell and can't run `dnx.cmd`.
- **Nothing on stdout but no tools either.** Logs go to stderr; your client usually shows them in its MCP log view.
- **Corporate NuGet feed.** `dnx` uses your NuGet configuration. If nuget.org is blocked, install once with
  `dotnet tool install -g DotNetDevMCP --add-source <feed>` and use `dotnetdevmcp` as the command.

## "No solution loaded"

Code-intelligence and affected-test tools need a loaded solution. Ask the agent to load it
(*"Load C:/src/MyApp/MyApp.sln"*) or start the server with `--load-solution`.

## Loading the solution is slow or fails

- Large multi-targeted solutions take 30 s or more: every target framework is a separate Roslyn project.
- Run `dotnet restore` first. Roslyn loads projects the way MSBuild sees them, so a solution that doesn't restore won't load.
- `.slnx` is supported.

## Affected tests

- **`selectionComplete: false`, whole solution ran.** Your change reaches code that too much depends on to trace within
  `maxSelectionSeconds` (default 10). This is by design. The first call of a session is also slower: run it again,
  or raise `maxSelectionSeconds`.
- **`ranWholeSolution: true` with a complete selection.** The selection was more than 20% of your tests, where a filtered
  run isn't faster. Change with `maxSelectedFraction`.
- **A test you expected isn't selected.** Check with `dryRun: true`. Reflection, string-keyed lookups and DI by convention
  are invisible to the walk. A very long call chain may need more than `maxDepth` (8) hops. Please report it with the
  `via` output: it helps improve the walk.
- **Slow on a multi-targeted solution.** Pass `framework: "net10.0"` (or ask *"net10.0 only"*): each target framework
  otherwise starts its own test host.

## Tests

- **"Zero tests ran" on a Microsoft.Testing.Platform repo.** The filter matched nothing; check the names with `dryRun`.
- **Run killed after 600 s.** A test hung. The error names the test modules that never finished; `timeoutSeconds`
  changes the limit.
- **Wrong SDK or test mode.** DotNetDevMCP runs `dotnet` from your project's directory, so the `global.json` there applies.
  If a test run behaves differently from your terminal, run the same command from that directory.

## Edits

- **`SharpTool_Undo` says it needs git integration.** Start the server with `--git-commit-edits`; undo reverts the commit
  each edit makes.
- **An edit reformatted my file.** Edit tools format only the lines they change, following your `.editorconfig`. If more
  changed, it's a bug: please report it.

## `--clean-env` and rejected values

- **A build or restore fails only with `--clean-env`.** Something it needs lives in an environment variable that isn't on the
  allow-list (a private feed token not named `NUGET_*`, or any other variable your build reads). Rename it to an allowed
  prefix, or run without `--clean-env` for that repository. `--log-level Debug` lists the names of the variables that were dropped.
- **A tool rejects my value** (framework, configuration, runtime, branch). Values are validated so they can't add options to
  `dotnet` or `git`: use plain names like `net10.0`, `Release`, `win-x64`, `feature/login`.

## Too many tools / token use

37 tools are on by default. Git and monitoring (16 more) are opt-in with `--enable`. See [Configuration](configuration.md).
