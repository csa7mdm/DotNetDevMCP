namespace DotNetDevMCP.CodeIntelligence.Services;

/// <summary>
/// Multi-targeted projects load once per TFM ("Polly.Core(net8.0)", "(net9.0)", ...). Work that's per-project-file
/// rather than per-TFM-variant (e.g. warming compilations) only needs the highest TFM of each; the others are the
/// same source compiled again. Mirrors the selection AffectedTestFinder.SearchScope makes for its own purposes.
/// </summary>
public static class ProjectTfmSelector {
    public static IEnumerable<Project> HighestTfmPerFile(IEnumerable<Project> projects) =>
        projects.GroupBy(p => p.FilePath ?? p.Name).Select(g => g.OrderByDescending(p => TfmVersion(p.Name)).First());

    /// <summary>"Polly.Core(net10.0)" -> 10.0; no TFM suffix -> 0.</summary>
    private static Version TfmVersion(string projectName) {
        var open = projectName.LastIndexOf("(net", StringComparison.Ordinal);
        if (open < 0) return new Version(0, 0);
        var digits = new string(projectName[(open + 4)..].TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return Version.TryParse(digits.Contains('.') ? digits : digits + ".0", out var v) ? v : new Version(0, 0);
    }
}
