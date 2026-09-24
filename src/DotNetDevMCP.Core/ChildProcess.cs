// Copyright (c) 2025 Ahmed Mustafa

using System.Diagnostics;

namespace DotNetDevMCP.Core;

/// <summary>
/// Shared setup for the dotnet/git child processes spawned by BuildService, GitService and TestRunner.
/// Today this only carries the opt-in environment scrub (<c>--clean-env</c>): when enabled, a child gets
/// a minimal allow-listed environment instead of inheriting the server's full one, so cloud credentials,
/// API keys and tokens sitting in the server's environment don't leak into every dotnet/git invocation.
/// This is a scrub, not a sandbox: an allow-listed child still has the same file-system and network
/// access as the user running the server.
/// </summary>
public static class ChildProcess
{
    /// <summary>Set once at startup from <c>--clean-env</c>. Off by default: children inherit the full environment.</summary>
    public static bool CleanEnvironment { get; set; }

    // Case-insensitive everywhere: env var names aren't case-sensitive to us, and Windows itself treats
    // them case-insensitively, so applying the same rule on every OS is simpler than special-casing it.
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    // Exact names dotnet/MSBuild/NuGet/git need to run at all, plus the handful of shell/user-profile
    // variables .NET and git consult (temp dirs, home dir, locale, process info).
    private static readonly string[] AllowedNames =
    [
        "PATH", "PATHEXT", "SystemRoot", "SYSTEMDRIVE", "windir", "ComSpec", "OS",
        "TEMP", "TMP", "TMPDIR", "HOME", "USERPROFILE", "HOMEDRIVE", "HOMEPATH",
        "APPDATA", "LOCALAPPDATA", "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
        "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "USERNAME", "USER", "LOGNAME", "LANG",
        // Restore behind a corporate proxy needs these (they may carry credentials, but without them NuGet can't reach a feed).
        "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "ALL_PROXY",
    ];

    // Prefixes rather than exact names: CommonProgramFiles/(x86)/W6432, every DOTNET_*/NUGET_*/MSBUILD*
    // tuning variable (including ones this process sets itself, like DOTNET_CLI_UI_LANGUAGE), and LC_*.
    private static readonly string[] AllowedPrefixes =
    [
        "CommonProgramFiles", "DOTNET_", "NUGET_", "MSBUILD", "LC_",
        "SSL_CERT_", // custom CA bundles on Linux/macOS
        "XDG_",      // NuGet and git config locations on Linux
    ];

    /// <summary>Applies the scrub to <paramref name="startInfo"/> if <see cref="CleanEnvironment"/> is on. No-op otherwise.</summary>
    public static void Prepare(ProcessStartInfo startInfo)
    {
        if (!CleanEnvironment) return;
        foreach (var name in startInfo.Environment.Keys.Where(k => !IsAllowed(k)).ToList())
        {
            startInfo.Environment.Remove(name);
        }
    }

    /// <summary>
    /// Reports what the scrub would do against this process's own environment, for the one-time startup
    /// log line (never per spawn: the allow-list is fixed, so the count doesn't change between children).
    /// </summary>
    public static (int Kept, int Dropped, IReadOnlyList<string> DroppedNames) DescribeEnvironment()
    {
        var names = Environment.GetEnvironmentVariables().Keys.Cast<string>().ToList();
        var dropped = names.Where(n => !IsAllowed(n)).OrderBy(n => n, NameComparer).ToList();
        return (names.Count - dropped.Count, dropped.Count, dropped);
    }

    private static bool IsAllowed(string name) =>
        AllowedNames.Contains(name, NameComparer)
        || AllowedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
