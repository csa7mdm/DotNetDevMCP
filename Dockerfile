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

# Non-root user. uid 10001 with a real home dir so `dotnet` (NuGet, MSBuild
# caches, etc.) has somewhere writable to put its own state; the mounted
# solution under /src is what the user actually controls.
#
# ~/.nuget/packages is pre-created and chowned here (not left for `dotnet` to
# create on first use) so that when a named volume is bind-mounted at that path
# (e.g. `-v nuget:/home/mcp/.nuget/packages`), Docker's first-use volume
# initialization copies this directory's mcp:mcp ownership into the volume,
# instead of creating it fresh as root because the path didn't previously exist
# in the image.
RUN useradd --uid 10001 --create-home --shell /usr/sbin/nologin mcp \
    && mkdir -p /home/mcp/.nuget/packages \
    && chown -R mcp:mcp /home/mcp

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

WORKDIR /src
USER mcp

ENTRYPOINT ["dotnet", "/app/dotnetdevmcp.dll"]
