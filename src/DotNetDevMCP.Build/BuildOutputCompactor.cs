// Copyright (c) 2025 Ahmed Mustafa

namespace DotNetDevMCP.Build;

/// <summary>
/// Pure helpers for turning a <see cref="BuildResult"/>'s diagnostics into an LLM-friendly,
/// bounded response: errors are always returned in full, warnings are de-duplicated (the same
/// code+file+line+message reported once per target framework or project counts only once) and
/// capped, and file paths are relativized to the project/solution directory when that is safe.
/// </summary>
public static class BuildOutputCompactor
{
    /// <summary>Default cap on the number of unique warnings returned in a compact response.</summary>
    public const int DefaultMaxWarnings = 20;

    /// <summary>A single diagnostic ready for serialization, with a relativized path.</summary>
    public sealed record CompactDiagnostic(
        DiagnosticSeverity Severity,
        string Code,
        string Message,
        string? FilePath,
        int? Line,
        int? Column);

    /// <summary>Result of compacting a build's diagnostics.</summary>
    public sealed record CompactedDiagnostics(
        IReadOnlyList<CompactDiagnostic> Errors,
        IReadOnlyList<CompactDiagnostic> Warnings,
        int WarningCount,
        int UniqueWarningCount,
        int WarningsTruncated);

    /// <summary>
    /// Splits <paramref name="diagnostics"/> into errors (all of them) and warnings (de-duplicated
    /// across target frameworks/projects, then capped at <paramref name="maxWarnings"/>).
    /// </summary>
    /// <param name="diagnostics">Raw diagnostics parsed from the build output.</param>
    /// <param name="baseDirectory">
    /// Directory that file paths should be made relative to (typically the project/solution
    /// directory). Pass null to leave paths untouched.
    /// </param>
    /// <param name="maxWarnings">Maximum number of unique warnings to include.</param>
    public static CompactedDiagnostics Compact(
        IEnumerable<BuildDiagnostic> diagnostics,
        string? baseDirectory = null,
        int maxWarnings = DefaultMaxWarnings)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var errors = new List<CompactDiagnostic>();
        var warningsRaw = new List<BuildDiagnostic>();

        foreach (var d in diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Error)
                errors.Add(ToCompact(d, baseDirectory));
            else if (d.Severity == DiagnosticSeverity.Warning)
                warningsRaw.Add(d);
        }

        var seen = new HashSet<(string Code, string? FilePath, int? Line, string Message)>();
        var uniqueWarnings = new List<BuildDiagnostic>();
        foreach (var w in warningsRaw)
        {
            var key = (w.Code, w.FilePath, w.Line, w.Message);
            if (seen.Add(key))
                uniqueWarnings.Add(w);
        }

        var shown = uniqueWarnings
            .Take(maxWarnings)
            .Select(d => ToCompact(d, baseDirectory))
            .ToList();

        var truncated = Math.Max(0, uniqueWarnings.Count - shown.Count);

        return new CompactedDiagnostics(
            Errors: errors,
            Warnings: shown,
            WarningCount: warningsRaw.Count,
            UniqueWarningCount: uniqueWarnings.Count,
            WarningsTruncated: truncated);
    }

    /// <summary>
    /// Makes <paramref name="filePath"/> relative to <paramref name="baseDirectory"/> when doing so
    /// is simple and safe (the path is rooted and shares a root with the base directory); otherwise
    /// returns the original path unchanged.
    /// </summary>
    public static string? RelativizePath(string? filePath, string? baseDirectory)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(baseDirectory))
            return filePath;

        if (!Path.IsPathRooted(filePath))
            return filePath;

        try
        {
            var relative = Path.GetRelativePath(baseDirectory, filePath);

            // Path.GetRelativePath falls back to returning an absolute path when the two paths
            // don't share a root (e.g. different drives on Windows) - only use the result when it
            // actually produced a relative path.
            return Path.IsPathRooted(relative) ? filePath : relative;
        }
        catch (ArgumentException)
        {
            // Invalid path characters, etc. - not worth failing the whole response over.
            return filePath;
        }
    }

    /// <summary>Returns the last <paramref name="maxLines"/> non-empty lines of <paramref name="output"/>.</summary>
    public static string Tail(string output, int maxLines = 20)
    {
        if (string.IsNullOrEmpty(output))
            return output ?? string.Empty;

        var lines = output
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Trim().Length > 0)
            .ToArray();

        return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - maxLines)));
    }

    private static CompactDiagnostic ToCompact(BuildDiagnostic d, string? baseDirectory) =>
        new(d.Severity, d.Code, d.Message, RelativizePath(d.FilePath, baseDirectory), d.Line, d.Column);
}
