using System.Net;

namespace DotNetDevMCP.Integration.Tests;

/// <summary>Real-server test for a server started with an extra <c>--allowed-origin</c>. A separate
/// server process/config from <see cref="LocalOriginGuardIntegrationTests"/>, so it gets its own
/// test class (and so its own InitializeAsync/DisposeAsync lifecycle).</summary>
public class LocalOriginGuardAllowedOriginIntegrationTests : LocalOriginGuardIntegrationTestsBase
{
    protected override string ExtraArgs => "--allowed-origin http://a";

    [Fact]
    public async Task Configured_allowed_origin_initialize_succeeds()
    {
        using var request = CreateInitializeRequest(Port, origin: "http://a");

        using var response = await Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
