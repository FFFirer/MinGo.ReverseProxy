using MinGo.Core.Routing;

namespace MinGo.Core.Tests.Routing;

/// <summary>
/// 匹配键归一化测试。口径来源是 YARP 实测（<c>MinGo.DataPlane.Tests.YarpMatchPrecedenceTests</c>）：
/// 域名与路径字面量大小写不敏感、末尾点多写一个不算新域名、catch-all 与空路径同义。
/// </summary>
public class RouteMatchKeyTests
{
    [Theory]
    [InlineData(null, RouteMatchKey.AnyHost)]
    [InlineData("", RouteMatchKey.AnyHost)]
    [InlineData("   ", RouteMatchKey.AnyHost)]
    [InlineData("  API.Example.COM. ", "api.example.com")]
    [InlineData("api.example.com...", "api.example.com")]
    [InlineData("*.Example.COM", "*.example.com")]
    public void NormalizeHost_TrimsLowersAndStripsTrailingDot(string? input, string expected)
    {
        Assert.Equal(expected, RouteMatchKey.NormalizeHost(input));
    }

    [Theory]
    [InlineData(null, RouteMatchKey.AnyPath)]
    [InlineData("", RouteMatchKey.AnyPath)]
    [InlineData("/*", RouteMatchKey.AnyPath)]
    [InlineData("/**", RouteMatchKey.AnyPath)]
    [InlineData("/{**catch-all}", RouteMatchKey.AnyPath)]
    [InlineData(" /{**Catch-All} ", RouteMatchKey.AnyPath)]
    [InlineData("/{**catchall}", RouteMatchKey.AnyPath)]
    [InlineData("/api/users", "/api/users")]
    [InlineData("/API/Users", "/api/users")]
    [InlineData("api/{**catch-all}", "/api/{**catch-all}")]
    public void NormalizePath_CollapsesCatchAllAndLowersLiterals(string? input, string expected)
    {
        Assert.Equal(expected, RouteMatchKey.NormalizePath(input));
    }

    [Fact]
    public void Of_WildcardAndExactHost_AreDistinctKeys()
    {
        // 精确域名优先命中，两者共存是有意的分层设计，不能判成重复
        Assert.NotEqual(
            RouteMatchKey.Of("*.example.com", "/api"),
            RouteMatchKey.Of("api.example.com", "/api"));
    }

    [Fact]
    public void Of_EmptyHostAndLiteralStar_AreTheSameKey()
    {
        Assert.Equal(RouteMatchKey.Of(null, "/api"), RouteMatchKey.Of("*", "/api"));
    }

    [Fact]
    public void Of_EmptyPathAndCatchAll_AreTheSameKey()
    {
        Assert.Equal(RouteMatchKey.Of("a.example.com", null), RouteMatchKey.Of("a.example.com", "/{**catch-all}"));
    }

    [Theory]
    [InlineData("api.example.com", "/v1/{**catch-all}", "api.example.com/v1/{**catch-all}")]
    [InlineData("API.example.com.", "/v1/*", "api.example.com/v1/*")]
    [InlineData("api.example.com", "", "api.example.com")]
    [InlineData(null, null, "*")]
    public void ToString_ReadsLikeTheRuleItDescribes(string? host, string? path, string expected)
    {
        Assert.Equal(expected, RouteMatchKey.Of(host, path).ToString());
    }
}
