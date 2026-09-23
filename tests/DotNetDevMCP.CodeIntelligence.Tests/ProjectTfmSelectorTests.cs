using DotNetDevMCP.CodeIntelligence.Services;
using Microsoft.CodeAnalysis;

namespace DotNetDevMCP.CodeIntelligence.Tests;

/// <summary>
/// Covers the project selection compilation warm-up uses to avoid rebuilding the same source once per TFM:
/// multi-targeted projects load once per TFM ("Polly.Core(net8.0)", "(net9.0)", ...) but share one project file.
/// </summary>
public class ProjectTfmSelectorTests {
    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "repo");

    [Fact]
    public void HighestTfmPerFile_picks_the_newest_tfm_variant_and_keeps_single_targeted_projects() {
        var solution = BuildSolution();

        var selected = ProjectTfmSelector.HighestTfmPerFile(solution.Projects).ToList();

        var multiTargeted = Assert.Single(selected, p => p.FilePath!.EndsWith("Multi.csproj"));
        Assert.Equal("Multi(net10.0)", multiTargeted.Name);

        var singleTargeted = Assert.Single(selected, p => p.FilePath!.EndsWith("Single.csproj"));
        Assert.Equal("Single", singleTargeted.Name);

        Assert.Equal(2, selected.Count);
    }

    private static Solution BuildSolution() {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        foreach (var tfm in new[] { "net8.0", "net9.0", "net10.0" }) {
            solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, $"Multi({tfm})", "Multi", LanguageNames.CSharp,
                filePath: Path.Combine(Root, "src", "Multi.csproj")));
        }

        solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Single", "Single", LanguageNames.CSharp,
            filePath: Path.Combine(Root, "src", "Single.csproj")));

        return solution;
    }
}
