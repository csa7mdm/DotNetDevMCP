---
title: "DotNetDevMCP"
description: "DotNetDevMCP is an open-source MCP server that gives AI coding agents Roslyn's compiler view of a .NET solution."
---

# DotNetDevMCP

DotNetDevMCP is an open-source MCP server that gives AI coding agents Roslyn's compiler view of a .NET solution: semantic
references, renames with a compile check, builds with compact output, and a tool that runs only the tests a change can break.

```bash
claude mcp add dotnetdevmcp -- dotnet dnx DotNetDevMCP --yes
```

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). MIT licensed.

![How DotNetDevMCP works](images/how-it-works.svg)

- [Documentation](docs/): installation, tutorial, tool reference, affected tests, configuration, troubleshooting, security
- [I benchmarked my .NET MCP server on Polly. It broke six ways.](articles/polly-benchmark/): the benchmark, the fixes and the limits
- [Source code and issues](https://github.com/csa7mdm/DotNetDevMCP) · [NuGet package](https://www.nuget.org/packages/DotNetDevMCP) · [llms.txt](llms.txt)
