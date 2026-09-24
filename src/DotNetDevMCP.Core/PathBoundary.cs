// Copyright (c) 2025 Ahmed Mustafa

namespace DotNetDevMCP.Core;

/// <summary>
/// Directory-boundary check shared by tools that must confirm a file lives under a given root
/// (e.g. the loaded solution directory). A plain <c>StartsWith</c> on the raw strings is fooled by
/// "..' traversal (<c>C:\src\App\..\..\x</c>) and by a sibling directory that merely shares a string
/// prefix (<c>C:\src\App-other\x</c> "starts with" <c>C:\src\App</c>); resolving both paths first and
/// comparing the relative path between them closes both holes.
/// </summary>
public static class PathBoundary
{
    /// <summary>
    /// True if <paramref name="path"/>, once fully resolved, is <paramref name="directory"/> itself or
    /// nested under it. Both inputs may be relative; they are resolved against the current directory
    /// the same way <see cref="Path.GetFullPath(string)"/> would. Comparison is ordinal (case-sensitive
    /// on Linux/macOS, case-insensitive on Windows) via <see cref="Path.GetRelativePath"/>, which already
    /// applies the right casing rule for the running OS.
    /// </summary>
    public static bool IsWithin(string path, string directory)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(directory)) return false;

        string fullPath, fullDirectory;
        try
        {
            fullPath = Path.GetFullPath(path);
            fullDirectory = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var relative = Path.GetRelativePath(fullDirectory, fullPath);
        // "." = same directory. Anything escaping the root comes back rooted (different drive) or starting
        // with "..": both cases mean the resolved path is outside, however the raw strings looked.
        return relative == "."
            || (relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !Path.IsPathRooted(relative));
    }
}
