---
title: "Configuration"
description: "Server options go after `--` in a `dnx` command, or straight after `dotnetdevmcp` for a global install:"
---
# Configuration

Server options go after `--` in a `dnx` command, or straight after `dotnetdevmcp` for a global install:

```bash
dotnet dnx DotNetDevMCP --yes -- --load-solution MyApp.sln --enable git
```

| Option | Default | What it does |
|---|---|---|
| `--load-solution <path>` | - | Load a `.sln` or `.slnx` at startup, so the agent doesn't need `SharpTool_LoadSolution` first |
| `--build-configuration <Debug\|Release>` | - | Configuration used when loading the solution |
| `--enable <groups>` | none | Turn on optional tool groups: `git` (10 tools), `monitoring` (6). Comma-separated or repeated: `--enable git,monitoring` |
| `--clean-env` | off | Start `dotnet` and `git` with a minimal environment (PATH, temp and profile folders, `DOTNET_*`, `NUGET_*`, `MSBUILD*` and the like) instead of inheriting yours, so tokens, API keys and cloud credentials in environment variables aren't passed on. Not a sandbox: see [Security](security.md) |
| `--git-commit-edits` | off | Edit tools create a `sharptools/<timestamp>` branch and commit each change; enables `SharpTool_Undo` |
| `--http` | off | Serve Streamable HTTP instead of stdio |
| `--port <n>` | 3001 | Port for `--http` |
| `--log-level <level>` | Information | `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal` |
| `--log-directory <dir>` | - | Also write rolling log files there. Logs always go to stderr, never stdout (stdout is the MCP channel) |
| `--version` | | Print the version |
| `--disable-git` | | Deprecated, has no effect |

## Why git and monitoring are off by default

Every tool the server registers is described to the agent in every session, which costs context tokens. An agent already
has a shell for `git status` and `git commit`, and the monitoring tools describe the server process, not your code. So
they're opt-in. Enable them if your client has no shell, or if you want the agent to stick to MCP tools.

## Per-call options

Test and build behavior is set per call, by the agent, through tool parameters: for example `framework`, `maxDepth` and
`timeoutSeconds` on the testing tools, `verbose` on `dotnet_build`. See [Tools Reference](tools-reference.md) and
[Affected Tests](affected-tests.md). You can ask for them in plain language: *"run the affected tests, net10.0 only"*.

## HTTP mode

```bash
dotnetdevmcp --http --port 3001 --load-solution MyApp.sln
```

The MCP endpoint is `http://localhost:3001/`. It listens on localhost only and has no authentication, TLS or origin checks: don't expose it. See [Security](security.md).
