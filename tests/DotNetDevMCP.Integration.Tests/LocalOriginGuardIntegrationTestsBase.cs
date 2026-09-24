using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DotNetDevMCP.Integration.Tests;

/// <summary>
/// Starts the real dotnetdevmcp --http host (via the project reference to DotNetDevMCP.Server) as a
/// child process on a free port, and hits it over real HTTP - no fakes, so this exercises the actual
/// Kestrel pipeline including our origin/host guard. One process per test method (xUnit creates a new
/// instance of the test class, and so calls InitializeAsync/DisposeAsync, for every [Fact]).
/// </summary>
public abstract class LocalOriginGuardIntegrationTestsBase : IAsyncLifetime
{
    /// <summary>Extra CLI args appended after "--http --port &lt;port&gt;". Override to test e.g. --allowed-origin.</summary>
    protected virtual string ExtraArgs => "";

    private Process? _process;
    private readonly StringBuilder _stderr = new();
    private readonly object _stderrLock = new();

    protected int Port { get; private set; }
    protected HttpClient Http { get; } = new();

    public async Task InitializeAsync()
    {
        const int maxAttempts = 2;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await StartOnceAsync();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                // If InitializeAsync throws, xUnit 2.9.3 never calls DisposeAsync for this instance,
                // so the child process (if one got started) would otherwise leak. Kill it here.
                KillQuietly();
                if (attempt == maxAttempts)
                {
                    throw new InvalidOperationException($"Failed to start the server after {maxAttempts} attempt(s).", lastError);
                }
                // Retry once more on a fresh port - covers a losing race for the port we picked
                // (another process binds it between us releasing the listener and the server binding it).
            }
        }

        throw lastError ?? new InvalidOperationException("unreachable");
    }

    private async Task StartOnceAsync()
    {
        Port = GetFreeTcpPort();

        var dllPath = Path.Combine(AppContext.BaseDirectory, "dotnetdevmcp.dll");
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException($"Expected the referenced server build at {dllPath}");
        }

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{dllPath}\" --http --port {Port} {ExtraArgs}".TrimEnd(),
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_stderrLock) _stderr.AppendLine(e.Data);
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitForPortAsync(Port, TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync()
    {
        Http.Dispose();
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

    private void KillQuietly()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // We're already failing; don't let cleanup mask the original error.
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    private async Task WaitForPortAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"Server process exited early (exit code {_process.ExitCode}) before listening on port {port}. Stderr:\n{ReadStderr()}");
            }

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(250);
            }
        }

        throw new TimeoutException(
            $"Server did not start listening on port {port} within {timeout}. Stderr:\n{ReadStderr()}");
    }

    private string ReadStderr()
    {
        lock (_stderrLock) return _stderr.ToString();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Builds a real MCP <c>initialize</c> POST, exactly what a compliant Streamable HTTP
    /// client sends to establish a session, so a 200 here proves the request actually reached the MCP
    /// endpoint rather than just failing to be a 403.</summary>
    protected static HttpRequestMessage CreateInitializeRequest(int port, string? origin = null, string? hostOverride = null)
    {
        const string body = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}}
            """;

        var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }
        if (hostOverride is not null)
        {
            // Overrides the Host header actually sent on the wire; the TCP connection still goes to
            // localhost:port via the request URI, simulating DNS rebinding (attacker DNS resolves
            // evil.example to 127.0.0.1, but the browser still sends "Host: evil.example").
            request.Headers.Host = hostOverride;
        }
        return request;
    }
}
