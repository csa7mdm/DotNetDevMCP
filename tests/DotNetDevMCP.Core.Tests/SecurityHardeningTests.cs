using System.Diagnostics;
using DotNetDevMCP.Core;

namespace DotNetDevMCP.Core.Tests;

/// <summary>The 0.3.2 hardening: values that name things can't smuggle options into dotnet/git, paths can't escape a root,
/// and --clean-env keeps secrets out of child processes.</summary>
public class SecurityHardeningTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net8.0-windows")]
    [InlineData("netstandard2.0")]
    public void Real_frameworks_are_accepted(string framework) => Assert.Null(DotnetArgumentValidation.ValidateFramework(framework));

    [Theory]
    [InlineData("net10.0 --logger:x")]
    [InlineData("--logger:x")]
    [InlineData("net10.0;-p:X=1")]
    public void Frameworks_that_would_add_options_are_rejected(string framework) => Assert.NotNull(DotnetArgumentValidation.ValidateFramework(framework));

    [Fact]
    public void Runtime_configuration_and_property_names_are_validated()
    {
        Assert.Null(DotnetArgumentValidation.ValidateRuntime("win-x64"));
        Assert.NotNull(DotnetArgumentValidation.ValidateRuntime("win-x64 --self-contained"));
        Assert.Null(DotnetArgumentValidation.ValidateConfiguration("Release"));
        Assert.NotNull(DotnetArgumentValidation.ValidateConfiguration("Release -p:X=1"));
        Assert.Null(DotnetArgumentValidation.ValidatePropertyName("Version"));
        Assert.NotNull(DotnetArgumentValidation.ValidatePropertyName("X=1 -p:CustomBeforeMicrosoftCommonTargets"));
    }

    [Theory]
    [InlineData("1.0;CustomBeforeMicrosoftCommonTargets=C:/evil.targets")]
    [InlineData("1.0,CustomBeforeMicrosoftCommonTargets=C:/evil.targets")]
    public void Property_values_cannot_start_a_second_property(string value)
    {
        var escaped = DotnetArgumentValidation.EscapePropertyValue(value);
        Assert.DoesNotContain(';', escaped);
        Assert.DoesNotContain(',', escaped);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("feature/login")]
    [InlineData("HEAD~3")]
    [InlineData("origin")]
    public void Real_git_refs_are_accepted(string value) => Assert.Null(GitRefValidation.Validate(value, "ref"));

    [Theory]
    [InlineData("--output=C:/x.txt")]
    [InlineData("--upload-pack=touch /tmp/x")]
    [InlineData("-c")]
    [InlineData("main\nrm")]
    [InlineData("")]
    public void Git_refs_that_would_be_options_are_rejected(string value) => Assert.NotNull(GitRefValidation.Validate(value, "ref"));

    private static readonly string Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "repo", "src", "App");

    [Fact]
    public void Paths_inside_the_root_are_within_it()
    {
        Assert.True(PathBoundary.IsWithin(Path.Combine(Root, "Orders", "OrderService.cs"), Root));
        Assert.True(PathBoundary.IsWithin(Root, Root));
        Assert.True(PathBoundary.IsWithin(Root + Path.DirectorySeparatorChar, Root));
        Assert.True(PathBoundary.IsWithin(Path.Combine(Root, "..foo", "x.cs"), Root)); // a folder named "..foo" is inside
    }

    [Fact]
    public void Traversal_and_look_alike_siblings_are_outside()
    {
        Assert.False(PathBoundary.IsWithin(Path.Combine(Root, "..", "..", "secrets.txt"), Root));
        Assert.False(PathBoundary.IsWithin(Path.Combine(Root + "-other", "x.cs"), Root));
        Assert.False(PathBoundary.IsWithin(Path.GetDirectoryName(Root)!, Root));
        Assert.False(PathBoundary.IsWithin("", Root));
    }

    [Fact]
    public void Relative_paths_are_resolved_before_comparing()
    {
        var relativeRoot = Path.Combine("rel", "App");
        Assert.True(PathBoundary.IsWithin(Path.Combine(relativeRoot, "x.cs"), relativeRoot));
        Assert.False(PathBoundary.IsWithin(Path.Combine(relativeRoot, "..", "Other", "x.cs"), relativeRoot));
    }

    [Fact]
    public void Clean_env_drops_secrets_and_keeps_what_dotnet_needs()
    {
        var psi = StartInfoWith(("PATH", "/bin"), ("DOTNET_CLI_UI_LANGUAGE", "en"), ("HTTPS_PROXY", "http://proxy:8080"),
            ("NUGET_PACKAGES", "/nuget"), ("AWS_SECRET_ACCESS_KEY", "secret"), ("GITHUB_TOKEN", "ghp_x"), ("OPENAI_API_KEY", "sk-x"));
        try
        {
            ChildProcess.CleanEnvironment = true;
            ChildProcess.Prepare(psi);
        }
        finally { ChildProcess.CleanEnvironment = false; }

        Assert.True(psi.Environment.ContainsKey("PATH"));
        Assert.True(psi.Environment.ContainsKey("DOTNET_CLI_UI_LANGUAGE"));
        Assert.True(psi.Environment.ContainsKey("HTTPS_PROXY"));
        Assert.True(psi.Environment.ContainsKey("NUGET_PACKAGES"));
        Assert.False(psi.Environment.ContainsKey("AWS_SECRET_ACCESS_KEY"));
        Assert.False(psi.Environment.ContainsKey("GITHUB_TOKEN"));
        Assert.False(psi.Environment.ContainsKey("OPENAI_API_KEY"));
    }

    [Fact]
    public void Clean_env_is_off_by_default()
    {
        var psi = StartInfoWith(("GITHUB_TOKEN", "ghp_x"));
        ChildProcess.Prepare(psi);
        Assert.True(psi.Environment.ContainsKey("GITHUB_TOKEN"));
    }

    private static ProcessStartInfo StartInfoWith(params (string Name, string Value)[] variables)
    {
        var psi = new ProcessStartInfo("dotnet");
        psi.Environment.Clear();
        foreach (var (name, value) in variables) psi.Environment[name] = value;
        return psi;
    }
}
