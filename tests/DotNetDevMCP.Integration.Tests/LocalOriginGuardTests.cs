using DotNetDevMCP.Server;

namespace DotNetDevMCP.Integration.Tests;

/// <summary>Unit tests for the pure decision logic behind --http's Origin/Host validation
/// (DNS rebinding and cross-origin browser request defense). No server involved here -
/// see <see cref="LocalOriginGuardIntegrationTests"/> for the real-host version.</summary>
public class LocalOriginGuardTests
{
    private const int Port = 3001;
    private static readonly string[] NoExtraOrigins = [];

    [Fact]
    public void Allowed_localhost_origin_is_accepted()
    {
        Assert.Null(LocalOriginGuard.Reject("http://localhost:3001", "localhost:3001", Port, NoExtraOrigins));
    }

    [Fact]
    public void Foreign_origin_is_rejected()
    {
        Assert.NotNull(LocalOriginGuard.Reject("http://evil.example", "localhost:3001", Port, NoExtraOrigins));
    }

    [Fact]
    public void Missing_origin_is_accepted_for_a_loopback_host()
    {
        // Non-browser MCP clients don't send an Origin header at all.
        Assert.Null(LocalOriginGuard.Reject(null, "localhost:3001", Port, NoExtraOrigins));
    }

    [Fact]
    public void Rebinding_host_with_no_origin_is_rejected()
    {
        Assert.NotNull(LocalOriginGuard.Reject(null, "evil.example:3001", Port, NoExtraOrigins));
    }

    [Fact]
    public void Loopback_ipv4_host_is_accepted()
    {
        Assert.Null(LocalOriginGuard.Reject(null, "127.0.0.1:3001", Port, NoExtraOrigins));
    }

    [Fact]
    public void Loopback_ipv6_host_is_accepted()
    {
        Assert.Null(LocalOriginGuard.Reject(null, "[::1]:3001", Port, NoExtraOrigins));
    }

    [Fact]
    public void Configured_extra_origin_is_accepted()
    {
        Assert.Null(LocalOriginGuard.Reject("http://localhost:5173", "localhost:3001", Port, ["http://localhost:5173"]));
    }

    [Fact]
    public void Similar_looking_lookalike_origin_is_rejected()
    {
        // "localhost.evil.example" contains "localhost" as a prefix but is a different host entirely.
        Assert.NotNull(LocalOriginGuard.Reject("http://localhost.evil.example:3001", "localhost:3001", Port, NoExtraOrigins));
    }

    [Fact]
    public void Missing_or_empty_host_header_is_allowed_as_a_non_browser_client()
    {
        // Kestrel does not reject a missing/empty Host header for us, so this does reach the guard.
        // We allow it: every real browser sends Host, so its absence means a non-browser client,
        // which isn't the DNS-rebinding threat this check defends against.
        Assert.Null(LocalOriginGuard.Reject(null, null, Port, NoExtraOrigins));
        Assert.Null(LocalOriginGuard.Reject(null, "", Port, NoExtraOrigins));
    }
}
