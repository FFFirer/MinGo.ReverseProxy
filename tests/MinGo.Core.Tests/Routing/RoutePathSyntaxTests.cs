using MinGo.Core.Routing;

namespace MinGo.Core.Tests.Routing;

/// <summary>
/// 匹配路径语法校验测试。解析器与 YARP 同一套（RoutePatternFactory），
/// 非法路径会让 YARP 拒绝整份代理配置（MinGo.DataPlane.Tests 曾用 spike 实测复现）。
/// </summary>
public class RoutePathSyntaxTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("/v1/*")]
    [InlineData("/api/{id}")]
    [InlineData("/{**catch-all}")]
    [InlineData("/{**catchall}")]
    public void IsValid_AcceptsCommonForms(string? path)
    {
        Assert.True(RoutePathSyntax.IsValid(path, out var error), error);
    }

    [Theory]
    [InlineData("api")]                  // 缺前导斜杠
    [InlineData("/{**catch-all)")]        // 右括号写成圆括号，缺 }
    [InlineData("/{id")]                  // 参数未闭合
    [InlineData("/a/{id}/b/{")]           // 空参数
    public void IsValid_RejectsMalformedPatterns(string path)
    {
        Assert.False(RoutePathSyntax.IsValid(path, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
