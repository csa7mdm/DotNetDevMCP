using System.Net;

namespace DotNetDevMCP.Integration.Tests;

/// <summary>Real-server tests for the default (no extra <c>--allowed-origin</c>) configuration.</summary>
public class LocalOriginGuardIntegrationTests : LocalOriginGuardIntegrationTestsBase
{
    [Fact]
    public async Task Foreign_origin_post_is_rejected_with_403()
    {
        using var request = CreateInitializeRequest(Port, origin: "http://evil.example");

        using var response = await Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rebinding_host_header_is_rejected_with_403()
    {
        using var request = CreateInitializeRequest(Port, hostOverride: "evil.example");

        using var response = await Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task No_origin_initialize_reaches_mcp_and_succeeds()
    {
        // No Origin header at all - simulates a non-browser MCP client performing a real handshake.
        using var request = CreateInitializeRequest(Port);

        using var response = await Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
