// Copyright (c) 2025 Ahmed Mustafa
// Validates and normalizes --allowed-origin values before they're handed to LocalOriginGuard, so a
// typo'd or nonsensical value (a path, a wildcard, "null") fails fast at startup instead of silently
// never matching (or, worse, matching more than intended).

namespace DotNetDevMCP.Server;

/// <summary>Validates a single <c>--allowed-origin</c> value.</summary>
public static class AllowedOriginValidation
{
    /// <summary>
    /// Checks that <paramref name="value"/> is an absolute <c>http</c>/<c>https</c> URI with nothing
    /// but scheme, host and optional port - no userinfo, path (other than an implicit trailing "/"),
    /// query or fragment - and isn't the literal string "null" or a wildcard.
    /// </summary>
    /// <param name="value">The raw value from <c>--allowed-origin</c>.</param>
    /// <param name="normalized">
    /// On success, the value normalized to a bare origin with no trailing slash (e.g.
    /// <c>http://localhost:5173/</c> -&gt; <c>http://localhost:5173</c>). On failure, echoes
    /// <paramref name="value"/> back unchanged.
    /// </param>
    /// <returns>A short reason the value is invalid, or null if it's valid.</returns>
    public static string? Validate(string value, out string normalized)
    {
        normalized = value;

        if (string.IsNullOrWhiteSpace(value))
        {
            return "must not be empty.";
        }

        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return "'null' is the serialized Origin of an opaque/sandboxed page, not a real origin - it can't be used here.";
        }

        if (value.Contains('*'))
        {
            return "wildcards are not allowed; give the exact origin.";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return "must be an absolute URI, e.g. http://localhost:5173.";
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return $"scheme must be http or https, not '{uri.Scheme}'.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "must not include a userinfo (user:pass@) component.";
        }

        if (uri.AbsolutePath != "/")
        {
            return "must not include a path.";
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            return "must not include a query string.";
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            return "must not include a fragment.";
        }

        // Scheme + host [+ port], no trailing slash - matches how a browser formats the Origin header.
        normalized = uri.GetLeftPart(UriPartial.Authority);
        return null;
    }
}
