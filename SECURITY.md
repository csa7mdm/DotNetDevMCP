# Security Policy

## Supported Versions

Currently, only the latest version of DotNetDevMCP is supported with security updates.

| Version | Supported          |
| ------- | ------------------ |
| 0.3.2+  | :white_check_mark: |
| 0.3.0-0.3.1 | :x: (argument injection, weak path check: upgrade) |
| < 0.3   | :x:                |

## Reporting a Vulnerability

We take the security of DotNetDevMCP seriously. If you discover a security vulnerability, please follow these steps:

### 1. **Do Not** Open a Public Issue

Please do not report security vulnerabilities through public GitHub issues.

### 2. Report Privately

Instead, please report security vulnerabilities using GitHub Security Advisories:

**Report**: [Create a Security Advisory](https://github.com/csa7mdm/DotNetDevMCP/security/advisories/new)

Alternatively, you can email the maintainer through your GitHub account or open a private issue.

Include the following information:
- Description of the vulnerability
- Steps to reproduce the issue
- Potential impact
- Suggested fix (if any)

### 3. Response Timeline

- **Initial Response**: Within 48 hours, we will acknowledge receipt of your report
- **Status Update**: Within 7 days, we will provide an initial assessment
- **Resolution**: We aim to release a fix within 30 days for critical vulnerabilities

### 4. Coordinated Disclosure

We practice coordinated disclosure:
- We will work with you to understand and validate the vulnerability
- Once a fix is ready, we will coordinate a release timeline
- We will publicly credit you for the discovery (unless you prefer to remain anonymous)

## Security model

DotNetDevMCP is a **local developer tool**. It runs as you, on your machine, for an AI agent you chose to trust with your
code. Read this before using it on code you don't trust or exposing it beyond your own machine.

### What it can do on your machine

- **Run code.** `dotnet build` and `dotnet test` execute whatever the solution contains: MSBuild targets (`<Exec>`), build
  tasks, source generators, test code. If the agent (or text in the repository steering the agent, i.e. prompt injection)
  writes a malicious test or `.csproj`, running the build runs it. This is true of `dotnet build` in any terminal; the
  server adds no sandbox.
- **Read and write files.** Roslyn edit tools write only inside the loaded solution's directory (paths are normalized, so
  `..` and look-alike sibling folders are rejected). Build, test and git tools accept any path you or the agent give them.
- **See your environment.** Child processes inherit the server's environment variables, including tokens and cloud
  credentials, unless you start the server with `--clean-env`.

For a local agent that already has a shell (Claude Code, Cursor, Copilot agent mode), none of this is new power: the agent
could run the same commands itself. What the server guarantees is that its own tools don't widen that: arguments are passed
to `dotnet` and `git` as separate arguments and validated (framework, configuration, runtime, git refs), so a crafted value
can't add options such as `-p:CustomBeforeMicrosoftCommonTargets=...` or `--output=...`.

### Options that reduce exposure

| Option | What it does | What it does not do |
|---|---|---|
| Default (git and monitoring tools off) | Fewer tools for the agent to misuse | - |
| `--clean-env` | Child processes get a minimal environment: tokens, API keys and cloud credentials in environment variables are not passed on | Not a sandbox: files such as `~/.aws/credentials` and the network are still reachable |
| Edits stay in the solution directory | Roslyn edit tools refuse paths outside it | Doesn't restrict what a build does |
| `--http`'s built-in Origin/Host checks | Blocks DNS rebinding and cross-origin browser requests to the port; `--allowed-origin` extends the allow-list for a trusted local dev server | Not authentication: any loopback process, browser tab on an allowed origin, or non-browser client can still reach it |

### Untrusted code

For repositories you don't trust (a pull request from a stranger, a downloaded sample), run the agent and DotNetDevMCP inside a
container or VM with only that repository mounted, no credentials, and restricted network. The server cannot provide that
isolation itself.

### `--http` mode

HTTP mode listens on `localhost` only. It rejects cross-origin browser requests and non-localhost Host headers (DNS
rebinding defense): a request with an `Origin` header that isn't localhost, `127.0.0.1`, `[::1]`, or a configured
`--allowed-origin` gets a 403, as does a request whose `Host` header doesn't name this machine's loopback interface.
Non-browser MCP clients, which don't send an `Origin` header, are unaffected. It still has **no authentication or TLS**:
anyone on the loopback interface who can reach the port can build, test and edit with your privileges. Don't forward the
port, put it behind a proxy, or run it on a shared machine. A multi-user or hosted deployment would need authentication,
a sandbox per session and audit logging; DotNetDevMCP doesn't provide those today.

## Security Updates

Security updates will be released as:
- Patch versions for non-breaking security fixes (0.3.x)
- Minor versions for breaking security fixes (0.x.0)

All security updates will be:
- Documented in the CHANGELOG
- Announced in GitHub Releases
- Tagged with `security` label

## Security Hall of Fame

We recognize and thank security researchers who responsibly disclose vulnerabilities:

*(No vulnerabilities reported yet)*

---

**Last Updated**: September 24, 2026
