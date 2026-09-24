using DotNetDevMCP.Testing;

namespace DotNetDevMCP.Testing.Tests;

public class RunnerArgumentTests
{
    [Theory]
    [InlineData("--filter-class My.Tests.OrderTests")]
    [InlineData("--filter-method \"My.Tests.OrderTests.Submit works\"")]
    [InlineData("--filter \"FullyQualifiedName~Orders\"")]
    [InlineData("--treenode-filter /*/*/OrderTests/*")]
    [InlineData("--treenode-filter /MyAssembly/My.Tests/*")]
    public void Filter_options_are_allowed_under_the_testing_platform(string filter) =>
        Assert.Null(TestRunner.ValidateTestingPlatformFilter(filter));

    [Theory]
    [InlineData("--filter-class X -p:CustomBeforeMicrosoftCommonTargets=C:/evil.targets")]
    [InlineData("/p:CustomBeforeMicrosoftCommonTargets=C:/evil.targets")]
    [InlineData("/p:X")]
    [InlineData("--results-directory C:/elsewhere")]
    [InlineData("--property:X=1")]
    public void Anything_else_is_rejected_under_the_testing_platform(string filter) =>
        Assert.NotNull(TestRunner.ValidateTestingPlatformFilter(filter));

    [Fact]
    public void VsTest_filter_widens_to_classes_then_to_no_filter()
    {
        var few = new[] { "Ns.C.A", "Ns.C.B" };
        Assert.Equal("FullyQualifiedName=Ns.C.A|FullyQualifiedName=Ns.C.B", TestRunner.WidenVsTestFilter(few));

        var sameClass = Enumerable.Range(0, 2000).Select(i => $"Some.Long.Namespace.Tests.RetryTests.Method_{i}").ToList();
        Assert.Equal("FullyQualifiedName~Some.Long.Namespace.Tests.RetryTests.", TestRunner.WidenVsTestFilter(sameClass));

        var manyClasses = Enumerable.Range(0, 2000).Select(i => $"Some.Long.Namespace.Tests.Class_{i}.Method").ToList();
        Assert.Null(TestRunner.WidenVsTestFilter(manyClasses));
    }
}
