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
runtime). No registry image is published yet, so build it locally, pinned to a release tag:

```bash
docker build -t dotnetdevmcp https://github.com/csa7mdm/DotNetDevMCP.git#v0.3.3
```

Restore needs network access, so do it once, separately, with every other sandbox flag still on, before locking the network
down too:

```bash
docker run --rm \
  -v "${PWD}:/src" -v dotnetdevmcp-nuget:/home/mcp/.nuget/packages \
  --memory 8g --memory-swap 8g --pids-limit 512 --cap-drop ALL --security-opt no-new-privileges \
  --entrypoint dotnet \
  dotnetdevmcp restore /src/YourSolution.sln
```

Then run the server itself with network disabled too, and the package cache mounted read-only:

```bash
claude mcp add dotnetdevmcp -- docker run -i --rm --network none -v "${PWD}:/src" -v dotnetdevmcp-nuget:/home/mcp/.nuget/packages:ro --memory 8g --memory-swap 8g --pids-limit 512 --cap-drop ALL --security-opt no-new-privileges dotnetdevmcp --load-solution /src/YourSolution.sln
```

**On native Linux** (unlike Docker Desktop on Windows/macOS, which runs everything inside its own VM and remaps ownership),
the checked-out repository on the host is owned by your own uid, and the image's built-in `mcp` user is a fixed uid 10001 -
so `dotnet build` can't create `obj/`/`bin/` under the mounted `/src`. Match the container to your own uid/gid instead, and
point the NuGet cache at `/nuget` (mode 1777, baked into the image specifically so any uid can use it):

```bash
docker run -i --rm --network none \
  --user "$(id -u):$(id -g)" -e HOME=/tmp -e NUGET_PACKAGES=/nuget \
  -v "${PWD}:/src" -v dotnetdevmcp-nuget:/nuget:ro \
  --memory 8g --memory-swap 8g --pids-limit 512 --cap-drop ALL --security-opt no-new-privileges \
  dotnetdevmcp --load-solution /src/YourSolution.sln
```

(restore the same way, minus `--network none` and the `:ro`). The `sandbox` job in `.github/workflows/build.yml` runs on a
native Linux GitHub-hosted runner and uses exactly this form, so it is verified in CI rather than only documented.

What each flag buys you:

| Flag | Blocks |
|---|---|
| `--network none` | Outbound network access from the server and anything it spawns (`dotnet build`, `dotnet test`, MSBuild tasks, source generators). |
| `--memory 8g` together with `--memory-swap 8g` | Unbounded memory growth; the kernel OOM-kills the container's processes instead of exhausting the host. Setting only `--memory` is not enough - Docker then defaults `--memory-swap` to *twice* `--memory`, so the container can still swap its way to 16 GiB. Setting both to the same value collapses the extra swap allowance to zero. |
| `--pids-limit 512` | Fork bombs and runaway process spawning inside the container. Note this counts *threads*, not just processes - `dotnet build`'s own thread-per-core parallelism on this repo peaked at 213 live threads on a 12-CPU runner during a full solution build, so raise this if you hit it on a larger machine or a much bigger solution, rather than treating 512 as universally safe headroom. |
| `--cap-drop ALL` | Empties the process's capability *bounding set*, not just the effective set - so even a setuid/setgid binary that tries to re-add a capability during `execve()` can't, because a process can never regain a capability outside its bounding set. |
| `--security-opt no-new-privileges` | Setuid/setgid binaries and similar mechanisms gaining privileges the container's own user doesn't have. The image's build also proactively strips the setuid/setgid bits from every file it ships (`chmod a-s`), so there's nothing left to try to use even without this flag - the flag is defense in depth against anything a future base-image update reintroduces. |

On Linux hosts, you can add `--runtime=runsc` to run the container under [gVisor](https://gvisor.dev/), which intercepts
syscalls in a userspace kernel instead of relying solely on the host kernel's namespace/cgroup isolation. This is a genuinely
stronger boundary, but it is Linux-hosts-only (no gVisor on Docker Desktop for Windows/macOS) and adds per-syscall overhead
that shows up directly in `dotnet build`/`test` latency; measure it against your own workload before adopting it as a default.

**Git mode inside the container.** The image runs `git config --system --add safe.directory '*'` at build time, because git
refuses to operate on a repository owned by a different uid than the process running it ("dubious ownership", a hardening
added after CVE-2022-24765) - and `/src` is a bind mount almost never owned by whichever uid the container runs as. This is
safe here specifically because the whole point of the container is to operate on whatever repo you mounted; it is *not* safe
to copy onto a host git config that trusts arbitrary repos. One thing it does not fix: a git **worktree or submodule** whose
`.git` file points at a gitdir outside `/src` (e.g. a worktree created from a bare repo that lives elsewhere on the host, or
a submodule whose superproject isn't mounted alongside it) still fails, because that gitdir path doesn't exist inside the
container. Only a self-contained repo (or worktree/submodule fully under the mounted directory) works in git mode.

`tests/Sandbox.Fixtures/` contains xUnit tests (`SandboxControlTests`) that assert these boundaries directly by reading the
kernel's own bookkeeping (`/proc/self/status`, `/sys/fs/cgroup/*`, `/proc/self/mountinfo`, `/sys/class/net`) rather than by
attempting an attack and timing out - e.g. asserting the capability bounding set is empty, `memory.max`/`memory.swap.max`
match the flags above, `pids.max` is 512, only the loopback network interface exists, and only `/src` and the package cache
are mounted from the host. They are not part of `DotNetDevMCP.sln` and are meant to be run inside the container, with and
without the sandbox flags, to prove the flags are doing something rather than being cargo-culted; running them without the
flags is expected to fail most of these assertions. CI runs both: the flagged version (which must pass) and, as a negative
control, the unflagged version (which must fail) in the `sandbox` job of `.github/workflows/build.yml`.

**What this setup does *not* protect against:**

- **The mounted repository is writable by design.** The whole point of the server is to build, test, and apply Roslyn edits
  to your solution, so `-v "${PWD}:/src"` is read-write on purpose. Anything that can reach that mount — the agent, a malicious
  test, a compromised MSBuild task — can still modify or delete your source. The container only contains *where else* it can
  reach, not what it can do to the code you handed it.
- **Only the MCP server is contained, not the agent driving it.** The MCP client (e.g. Claude Code) that runs `docker run`
  needs access to the Docker daemon to do so, which on Linux is root-equivalent (anyone who can talk to the daemon, e.g. via
  membership in the `docker` group, can mount `/` read-write and run as root inside a container). Sandboxing the server's own
  container does nothing to sandbox whatever launched it.
- **Restore needs network**, and while it has that access it runs the target solution's own MSBuild/NuGet logic - a
  malicious `.csproj`/`nuget.config` (pointing at an attacker-controlled feed, or a package with a build-time script) can use
  that same window to make outbound requests. Run restore with every other sandbox flag on (as shown above), only against
  solutions/feeds you already trust, and switch to `--network none` for the actual `--load-solution` run, which needs no
  network for anything the server itself does.
- **The shared NuGet cache volume is writable during restore**, and NuGet packages can carry MSBuild `.targets`/`.props`
  files that run during a build. Restoring one untrusted repository into `dotnetdevmcp-nuget` can plant such a file where a
  *different* repo's build - sharing that same cache volume - would then execute it. If you work with untrusted code, use a
  separate named volume per repository instead of one shared cache.
- **A container is not a hard security boundary on every host.** Docker containers share the host kernel; on Linux without
  gVisor (or an equivalent), a kernel exploit can still escape the container. On Docker Desktop (Windows/macOS) the shared
  boundary is one layer further out: every container runs inside the *same* Linux VM, so a kernel-level escape there reaches
  every other container in that VM, not just this one. Treat all of this as raising the cost of an attack (no ambient
  network, capped resources, no ambient host filesystem access beyond the mount), not as equivalent to a VM per container.

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
