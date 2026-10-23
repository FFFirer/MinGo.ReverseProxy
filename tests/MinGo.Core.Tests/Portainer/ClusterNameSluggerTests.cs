using MinGo.Core.Portainer;

namespace MinGo.Core.Tests.Portainer;

public class ClusterNameSluggerTests
{
    [Theory]
    [InlineData("staticpages", "staticpages")]
    [InlineData("mingo-reverseproxy", "mingo-reverseproxy")]
    [InlineData("My Stack", "my-stack")]
    [InlineData("  Foo//Bar  ", "foo-bar")]
    [InlineData("app_portal.v2", "app_portal.v2")]
    [InlineData("Aspire Dashboard", "aspire-dashboard")]
    [InlineData("---trim---", "trim")]
    [InlineData("a   b", "a-b")]
    public void Slug_NormalizesProjectName(string input, string expected)
    {
        Assert.Equal(expected, ClusterNameSlugger.Slug(input, "stack-4"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("项目一")]
    [InlineData("***")]
    [InlineData(null)]
    public void Slug_EmptyResult_FallsBack(string? input)
    {
        Assert.Equal("stack-4", ClusterNameSlugger.Slug(input, "stack-4"));
    }

    [Fact]
    public void Slug_FallbackIsAlsoNormalized()
    {
        Assert.Equal("stack-4", ClusterNameSlugger.Slug("***", " Stack 4 "));
    }

    [Fact]
    public void Slug_NoFallback_UsesUltimateFallback()
    {
        Assert.Equal("cluster", ClusterNameSlugger.Slug(null));
    }

    [Fact]
    public void Slug_PreservesNumericLegacyStackName()
    {
        // debian12 上存在 compose project 名为 "1" 的遗留部署。
        Assert.Equal("1", ClusterNameSlugger.Slug("1", "stack-2"));
    }
}
