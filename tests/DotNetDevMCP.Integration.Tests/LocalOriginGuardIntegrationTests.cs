using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DotNetDevMCP.Integration.Tests;

/// <summary>Starts the real dotnetdevmcp --http host (via the project reference to
/// DotNetDevMCP.Server) as a child process on a free port, and hits it over real HTTP -
/// no fakes, so this exercises the actual Kestrel pipeline including our origin/host guard.</summary>
public class LocalOriginGuardIntegrationTests : IAsyncLifetime
{
    private Process? _process;
    private int _port;
    private readonly HttpClient _http = new();

    public async Task InitializeAsync()
    {
        _port = GetFreeTcpPort();

        var dllPath = Path.Combine(AppContext.BaseDirectory, "dotnetdevmcp.dll");
        Assert.True(File.Exists(dllPath), $"Expected the referenced server build at {dllPath}");

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{dllPath}\" --http --port {_port}",
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitForPortAsync(_port, TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
        _process?.Dispose();
    }

    [Fact]
    public async Task Foreign_origin_post_is_rejected_with_403()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{_port}/")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Origin", "http://evil.example");

        using var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task No_origin_post_is_not_rejected_by_the_guard()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{_port}/")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        // No Origin header at all - simulates a non-browser MCP client.

        using var response = await _http.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForPortAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(250);
            }
        }
        throw new TimeoutException($"Server did not start listening on port {port} within {timeout}.", last);
    }
}
