using DotNetDevMCP.Build;

namespace DotNetDevMCP.Build.Tests;

public class BuildArgumentTests
{
    [Fact]
    public void Each_value_is_one_argument_so_a_property_value_cannot_add_switches()
    {
        var options = new BuildOptions(
            Configuration: "Release",
            Framework: "net10.0",
            Properties: new Dictionary<string, string> { ["Version"] = "1.0 -p:CustomBeforeMicrosoftCommonTargets=C:/evil.targets" });

        var args = BuildService.BuildArgumentList("build", Path.Combine(Path.GetTempPath(), "App.csproj"), options);

        Assert.Equal(1, args.Count(a => a.StartsWith("-p:", StringComparison.Ordinal)));
        Assert.Contains("-p:Version=1.0 -p:CustomBeforeMicrosoftCommonTargets=C:/evil.targets", args);
        Assert.Equal(["--configuration", "Release"], args.SkipWhile(a => a != "--configuration").Take(2));
        Assert.Equal(["--framework", "net10.0"], args.SkipWhile(a => a != "--framework").Take(2));
    }

    [Fact]
    public void Property_list_separators_are_escaped()
    {
        var options = new BuildOptions(Properties: new Dictionary<string, string> { ["Version"] = "1.0;Extra=1,More=2" });

        var args = BuildService.BuildArgumentList("build", Path.Combine(Path.GetTempPath(), "App.csproj"), options);

        Assert.Contains("-p:Version=1.0%3BExtra=1%2CMore=2", args);
    }
}
