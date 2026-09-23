using DotNetDevMCP.Testing;

namespace DotNetDevMCP.Testing.Tests;

public class TestingPlatformTests
{
    [Theory]
    [InlineData("""{ "test": { "runner": "Microsoft.Testing.Platform" } }""", true)]
    [InlineData("{ /* comment */ \"sdk\": { \"version\": \"10.0.100\" }, \"test\": { \"runner\": \"microsoft.testing.platform\" }, }", true)]
    [InlineData("""{ "test": { "runner": "VSTest" } }""", false)]
    [InlineData("""{ "sdk": { "version": "10.0.100" } }""", false)]
    public void Detects_mtp_mode_from_the_nearest_global_json(string globalJson, bool expected)
    {
        var root = Directory.CreateTempSubdirectory("mtp-detect").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "global.json"), globalJson);
            var project = Directory.CreateDirectory(Path.Combine(root, "test", "A.Tests")).FullName;
            var csproj = Path.Combine(project, "A.Tests.csproj");
            File.WriteAllText(csproj, "<Project />");

            Assert.Equal(expected, TestRunner.UsesTestingPlatform(csproj));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Name_filter_uses_each_frameworks_syntax()
    {
        string[] names = ["Ns.C.A", "Ns.C.B"];

        Assert.Equal(""" --filter-method "Ns.C.A" --filter-method "Ns.C.B" """.TrimEnd(), TestRunner.TestingPlatformNameFilter(names, xunit: true));
        Assert.Equal(""" --filter "FullyQualifiedName=Ns.C.A|FullyQualifiedName=Ns.C.B" """.TrimEnd(), TestRunner.TestingPlatformNameFilter(names, xunit: false));
        Assert.Equal("", TestRunner.TestingPlatformNameFilter([], xunit: true));
    }

    [Fact]
    public void Too_many_names_widen_to_classes_then_to_everything()
    {
        var sameClass = Enumerable.Range(0, 2000).Select(i => $"Some.Long.Namespace.Tests.RetryTests.Method_{i}").ToList();
        Assert.Equal(""" --filter-class "Some.Long.Namespace.Tests.RetryTests" """.TrimEnd(), TestRunner.TestingPlatformNameFilter(sameClass, xunit: true));

        var manyClasses = Enumerable.Range(0, 2000).Select(i => $"Some.Long.Namespace.Tests.Class_{i}.Method").ToList();
        Assert.Equal("", TestRunner.TestingPlatformNameFilter(manyClasses, xunit: true));
    }
}
