// Copyright (c) 2025 Ahmed Mustafa
// DNS-rebinding / cross-origin defense for --http mode, per the MCP Streamable HTTP transport
// security guidance: servers MUST validate the Origin header on incoming connections and SHOULD
// bind only to localhost. We already bind to localhost only; this adds the Origin/Host checks.

namespace DotNetDevMCP.Server;

/// <summary>
/// Rejects any request carrying a foreign <c>Origin</c> header, and any request whose <c>Host</c> header
/// doesn't name this machine's loopback interface, so a malicious page (via DNS rebinding or a plain
/// <c>fetch()</c>) can't reach the MCP server through a victim's browser. Requests without an Origin
/// header - every non-browser MCP client, and a browser's simple GET/HEAD - aren't rejected by this
/// check; they just get whatever the MCP endpoint itself returns for that request.
/// </summary>
public static class LocalOriginGuard
{
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    /// <summary>
    /// Pure decision logic, kept free of any ASP.NET Core types so it can be unit tested directly.
    /// Returns a short reason to reject the request with, or null to allow it.
    /// </summary>
    /// <param name="origin">The raw <c>Origin</c> header value, or null/empty if absent.</param>
    /// <param name="host">The raw <c>Host</c> header value (may include a port), or null/empty if absent.</param>
    /// <param name="port">The port this server is listening on.</param>
    /// <param name="extraOrigins">Additional allowed origins from <c>--allowed-origin</c>, exact strings.</param>
    public static string? Reject(string? origin, string? host, int port, IReadOnlyCollection<string> extraOrigins)
    {
        if (!string.IsNullOrEmpty(origin))
        {
            string[] builtIn =
            [
                $"http://localhost:{port}",
                $"http://127.0.0.1:{port}",
                $"http://[::1]:{port}",
            ];

            bool allowed = false;
            foreach (var candidate in builtIn)
            {
                if (string.Equals(origin, candidate, StringComparison.OrdinalIgnoreCase)) { allowed = true; break; }
            }
            if (!allowed)
            {
                foreach (var candidate in extraOrigins)
                {
                    if (string.Equals(origin, candidate, StringComparison.OrdinalIgnoreCase)) { allowed = true; break; }
                }
            }

            if (!allowed)
            {
                return $"Origin '{origin}' is not allowed. This server only accepts requests from localhost origins " +
                       "(plus any configured --allowed-origin).";
            }
        }

        // A missing or empty Host header does reach this code - Kestrel does not reject it for us
        // (an HTTP/1.0 request with no Host header, or one with an empty Host value, is passed through).
        // We allow it deliberately: every real browser always sends Host, so a request without one is
        // necessarily a non-browser client, which isn't the DNS-rebinding threat this check defends against.
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        var hostOnly = StripPort(host);
        foreach (var loopback in LoopbackHosts)
        {
            if (string.Equals(loopback, hostOnly, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return $"Host '{host}' is not allowed. This server only accepts requests addressed to localhost, " +
               "127.0.0.1 or [::1] (DNS rebinding protection).";
    }

    /// <summary>Strips the trailing ":port" from a Host header value. IPv6 literals keep their brackets
    /// (e.g. "[::1]:3001" -&gt; "[::1]") so they can be compared against <see cref="LoopbackHosts"/> as-is.</summary>
    private static string StripPort(string hostHeader)
    {
        if (hostHeader.StartsWith('['))
        {
            var end = hostHeader.IndexOf(']');
            return end < 0 ? hostHeader : hostHeader[..(end + 1)];
        }

        var colon = hostHeader.IndexOf(':');
        return colon < 0 ? hostHeader : hostHeader[..colon];
    }

    /// <summary>Registers the guard as middleware. Must run before <c>MapMcp()</c> so a rejected request
    /// never reaches the MCP transport.</summary>
    public static IApplicationBuilder UseLocalOriginGuard(this IApplicationBuilder app, int port, IReadOnlyCollection<string> extraOrigins)
    {
        return app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.Count > 0 ? context.Request.Headers.Origin.ToString() : null;
            var host = context.Request.Headers.Host.Count > 0 ? context.Request.Headers.Host.ToString() : null;

            var reason = Reject(origin, host, port, extraOrigins);
            if (reason is not null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(reason);
                return;
            }

            await next(context);
        });
    }
}
