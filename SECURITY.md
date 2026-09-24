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
| `--http`'s built-in Origin/Host checks | Rejects requests carrying a foreign `Origin` header and requests whose `Host` doesn't name this machine's loopback interface (DNS rebinding) | Not authentication, and sends no CORS headers: `--allowed-origin` doesn't let a browser page call it directly, only a proxy or non-browser client that sets that Origin; any request without an `Origin` header still reaches the port |

### Untrusted code

For repositories you don't trust (a pull request from a stranger, a downloaded sample), run the agent and DotNetDevMCP inside a
container or VM with only that repository mounted, no credentials, and restricted network. The server cannot provide that
isolation itself.

### Run it in a container

A `Dockerfile` at the repo root builds DotNetDevMCP into an image that runs as a non-root user (uid 10001) with the .NET SDK
available (the server shells out to `dotnet build`/`test`, so it needs the full SDK, not just the runtime, at container
runtime). No registry image is published yet, so build it locally:

```bash
docker build -t dotnetdevmcp https://github.com/csa7mdm/DotNetDevMCP.git
```

Restore needs network access, so do it once, separately, before locking the container down:

```bash
docker run --rm --network bridge \
  -v "$PWD:/src" -v dotnetdevmcp-nuget:/home/mcp/.nuget/packages \
  --entrypoint dotnet \
  dotnetdevmcp restore /src/YourSolution.sln
```

Then run the server itself with network disabled and the rest of the sandbox flags on:

```bash
claude mcp add dotnetdevmcp -- docker run -i --rm --network none -v "$PWD:/src" -v dotnetdevmcp-nuget:/home/mcp/.nuget/packages --memory 8g --pids-limit 512 --cap-drop ALL --security-opt no-new-privileges dotnetdevmcp --load-solution /src/YourSolution.sln
```

What each flag buys you:

| Flag | Blocks |
|---|---|
| `--network none` | Outbound network access from the server and anything it spawns (`dotnet build`, `dotnet test`, MSBuild tasks, source generators). |
| `--memory 8g` | Unbounded memory growth; the kernel OOM-kills the container's processes instead of exhausting the host. |
| `--pids-limit 512` | Fork bombs and runaway process spawning inside the container. |
| `--cap-drop ALL` | All Linux capabilities beyond the unprivileged default, including ones that could otherwise be used to escalate or interfere with the host. |
| `--security-opt no-new-privileges` | Setuid/setgid binaries and similar mechanisms gaining privileges the container's own user doesn't have. |

On Linux hosts, you can add `--runtime=runsc` to run the container under [gVisor](https://gvisor.dev/), which intercepts
syscalls in a userspace kernel instead of relying solely on the host kernel's namespace/cgroup isolation. This is a genuinely
stronger boundary, but it is Linux-hosts-only (no gVisor on Docker Desktop for Windows/macOS) and adds per-syscall overhead
that shows up directly in `dotnet build`/`test` latency; measure it against your own workload before adopting it as a default.

`tests/Sandbox.Fixtures/` contains xUnit tests (`ReadingSshKeysFails`, `WritingOutsideWorkspaceFails`, `NetworkEgressFails`,
`ForkBombIsCapped`, `MemoryIsCapped`) that exercise these boundaries directly: each attempts one attack and asserts it failed.
They are not part of `DotNetDevMCP.sln` and are meant to be run inside the container, with and without the sandbox flags, to
prove the flags are doing something rather than being cargo-culted. CI runs them under the exact flags above in the `sandbox`
job of `.github/workflows/build.yml`.

**What this setup does *not* protect against:**

- **The mounted repository is writable by design.** The whole point of the server is to build, test, and apply Roslyn edits
  to your solution, so `-v "$PWD:/src"` is read-write on purpose. Anything that can reach that mount — the agent, a malicious
  test, a compromised MSBuild task — can still modify or delete your source. The container only contains *where else* it can
  reach, not what it can do to the code you handed it.
- **Restore needs network.** The restore step above deliberately runs with `--network bridge` (or plain default networking)
  because NuGet has to reach package feeds. That is a real window for a malicious `.csproj`/`nuget.config` (e.g. pointing at
  an attacker-controlled feed, or a package with a build-time script) to make outbound requests. Only restore against
  solutions you already trust, and switch back to `--network none` for the actual `--load-solution` run, which needs no
  network for anything the server itself does.
- **A container is not a hard security boundary on every host.** Docker containers share the host kernel; on Linux without
  gVisor (or an equivalent), a kernel exploit can still escape the container. Treat this as raising the cost of an attack
  (no ambient network, capped resources, no ambient host filesystem access beyond the mount), not as equivalent to a VM.

### `--http` mode

HTTP mode listens on `localhost` only. A request that carries an `Origin` header is checked against an allow-list: only
`http://localhost:<port>`, `http://127.0.0.1:<port>` and `http://[::1]:<port>` (the server's own port), plus any exact
value passed via `--allowed-origin` (validated at startup - no wildcards, no path/query/fragment/userinfo), are accepted.
Everything else - including other localhost ports and `https://` origins not explicitly allow-listed - gets a 403. A
request whose `Host` header doesn't name this machine's loopback interface also gets a 403 (DNS rebinding defense).
Requests without an `Origin` header (every non-browser MCP client, and a browser's simple GET/HEAD) are not rejected by
this check; they still only get whatever the MCP endpoint itself returns for that request. The server sends no CORS
headers, so `--allowed-origin` does not let a browser page call it directly from that origin - it's for a local
dev-server proxy that forwards the original `Origin`, or a non-browser client that happens to set one. It still has
**no authentication or TLS**: anyone on the loopback interface who can reach the port can build, test and edit with your
privileges. Don't forward the port, put it behind a proxy, or run it on a shared machine. A multi-user or hosted
deployment would need authentication, a sandbox per session and audit logging; DotNetDevMCP doesn't provide those today.

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
