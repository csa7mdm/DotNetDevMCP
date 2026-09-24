---
title: Installation
description: You need the [.
---
# Installation

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`dotnet --version` shows 10.x). Your own
solution can target older frameworks; the SDK only has to be able to build it.

DotNetDevMCP runs through `dotnet dnx`, which downloads the package from NuGet on first use and runs it. Nothing to install
by hand. The examples use `"command": "dotnet"` with `dnx` as the first argument: it works on every OS and in clients that
start servers without a shell (a bare `dnx` command can fail there on Windows, where `dnx` is a `.cmd` script).

## Claude Code

```bash
claude mcp add dotnetdevmcp -- dotnet dnx DotNetDevMCP --yes
```

Load a solution at startup, so the first question is fast:

```bash
claude mcp add dotnetdevmcp -- dotnet dnx DotNetDevMCP --yes -- --load-solution /path/to/MyApp.sln
```

## VS Code (GitHub Copilot)

`.vscode/mcp.json` in your repository:

```json
{
  "servers": {
    "dotnetdevmcp": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["dnx", "DotNetDevMCP", "--yes"]
    }
  }
}
```

The [NuGet page](https://www.nuget.org/packages/DotNetDevMCP) has an **MCP Server** tab with a ready-made version of this
that asks for a solution path.

## Visual Studio

`.mcp.json` next to your solution (or `%USERPROFILE%\.mcp.json` for every solution), same content as VS Code above.

## Cursor, Claude Desktop and other clients

These use the `mcpServers` form. Cursor: `.cursor/mcp.json` or `~/.cursor/mcp.json`. Claude Desktop: Settings → Developer →
Edit config.

```json
{
  "mcpServers": {
    "dotnetdevmcp": {
      "command": "dotnet",
      "args": ["dnx", "DotNetDevMCP", "--yes", "--", "--load-solution", "C:/src/MyApp/MyApp.sln"]
    }
  }
}
```

Everything after `--` goes to DotNetDevMCP itself; see [Configuration](configuration.md).

## Permanent install instead of dnx

```bash
dotnet tool install -g DotNetDevMCP
```

Then use `dotnetdevmcp` as the command, with no `dnx` arguments. Update with `dotnet tool update -g DotNetDevMCP`.

## Pin a version

`dnx DotNetDevMCP` uses the latest version. For a team setup, pin it: `DotNetDevMCP@0.3.1`.

## Check it works

Ask your agent: *"Which DotNetDevMCP tools do you have?"* It should list 37 tools, such as `SharpTool_LoadSolution`,
`SharpTool_FindReferences` and `dotnet_test_affected`. If not, see [Troubleshooting](troubleshooting.md).

Next: [Tutorial](tutorial.md).
