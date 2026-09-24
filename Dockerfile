# syntax=docker/dockerfile:1
#
# Multi-stage build for DotNetDevMCP.
#
# The runtime stage uses the full SDK image (not aspnet/runtime) because the
# server itself shells out to `dotnet build` / `dotnet test` / `dotnet restore`
# against the caller's mounted solution - the SDK toolchain must be present
# at runtime, not just at build time.

# mcr.microsoft.com/dotnet/sdk:10.0
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
WORKDIR /repo
COPY . .
RUN dotnet publish src/DotNetDevMCP.Server -c Release -o /app

# mcr.microsoft.com/dotnet/sdk:10.0
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29
COPY --from=build /app /app

# Git treats a repository owned by a different uid than the process running it
# as "dubious ownership" and refuses to operate on it (CVE-2022-24765
# hardening). /src is a bind mount owned by whatever the host repo's owner is,
# almost never uid 10001, so without this the `git`-mode tools (and
# dotnet_test_affected's git diff) fail with "fatal: detected dubious
# ownership in repository at '/src'". Trusting every directory here is fine:
# the whole point of this container is to operate on whatever repo the caller
# mounted, which is already the trust boundary.
#
# Caveat this does NOT fix: a git worktree or submodule whose .git *file*
# points at a gitdir outside /src (e.g. a worktree checked out from a bare
# repo elsewhere on the host, or a submodule whose superproject isn't also
# mounted) still fails, because that gitdir path doesn't exist inside the
# container. Only self-contained repos/worktrees fully under the mounted
# directory work in git mode.
RUN git config --system --add safe.directory '*'

# Strip setuid/setgid bits from every regular file the SDK image ships (sudo,
# ping, etc.). Nothing in this server's own code needs them, and --cap-drop
# ALL already empties the capability bounding set, but removing the bits too
# means a setuid binary can't even try to regain a capability if some future
# base-image update reintroduces one. -xdev keeps this to the image's own
# filesystem layer (nothing is mounted yet at build time regardless).
RUN find / -xdev -perm /6000 -type f -exec chmod a-s {} +

# Non-root user. uid 10001 with a real home dir so `dotnet` (NuGet, MSBuild
# caches, etc.) has somewhere writable to put its own state; the mounted
# solution under /src is what the user actually controls.
#
# ~/.nuget/packages is pre-created and chowned here (not left for `dotnet` to
# create on first use) so that when a named volume is bind-mounted at that path
# (e.g. `-v nuget:/home/mcp/.nuget/packages`), Docker's first-use volume
# initialization copies this directory's mcp:mcp ownership into the volume,
# instead of creating it fresh as root because the path didn't previously exist
# in the image. That's the default (Docker Desktop / Windows / macOS) form.
#
# /nuget (mode 1777, sticky-bit world-writable, like /tmp) is a second,
# uid-agnostic package cache location for the native-Linux form documented in
# README/SECURITY.md, where the container is run with `--user "$(id -u):$(id
# -g)"` to match the host repo's owner instead of the image's built-in mcp
# user - in that form uid 10001 doesn't exist as far as the container's
# process is concerned, so ~mcp/.nuget isn't usable and NUGET_PACKAGES=/nuget
# is passed instead.
RUN useradd --uid 10001 --create-home --shell /usr/sbin/nologin mcp \
    && mkdir -p /home/mcp/.nuget/packages \
    && mkdir -m 1777 /nuget \
    && chown -R mcp:mcp /home/mcp

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

WORKDIR /src
USER mcp

ENTRYPOINT ["dotnet", "/app/dotnetdevmcp.dll"]
