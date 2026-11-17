using MinGo.Core.Models;
using MinGo.Core.Routing;

namespace MinGo.Core.Tests.Routing;

/// <summary>
/// 底层配置编译器测试。重点锁两条契约：
/// 绑定外部隧道的启用路由必须同样产出数据面规则（隧道不直连后端），
/// 以及同一匹配键只留一条、且谁留谁走是确定的。
/// </summary>
public class DataPlaneRuleCompilerTests
{
    private static RouteConfig Route(
        string id,
        string? host,
        string? path,
        bool enabled = true,
        string connectionId = ConnectionIds.Local,
        string clusterId = "c1") => new()
    {
        Id = id,
        Name = id,
        ClusterId = clusterId,
        ConnectionId = connectionId,
        Enabled = enabled,
        // RouteMatch.Host/Path 在非空引用下用空串表达「未填」，归一化后等价于任意匹配
        Match = new RouteMatch { Host = host ?? string.Empty, Path = path ?? string.Empty },
    };

    private static IReadOnlyList<string> Ids(DataPlaneRuleCompiler.Result result) =>
        result.Rules.Select(r => r.Id).ToArray();

    // ── 隧道不直连后端 ──

    [Fact]
    public void Compile_KeepsTunnelBoundRoutes()
    {
        // 契约守卫：接入通道只决定流量从哪里进来。隧道 ingress 一律回源到数据面，
        // 所以绑隧道的路由必须在数据面也有规则，否则隧道流量落地即 404。
        // 谁把这里改成「按 ConnectionId 过滤」，这条先红。
        var result = DataPlaneRuleCompiler.Compile([
            Route("t1", "api.example.com", "/api/{**catch-all}", connectionId: "tun-1"),
        ]);

        Assert.Equal(new[] { "t1" }, Ids(result));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Compile_TunnelAndLocalRoutes_BothProduceRules()
    {
        var result = DataPlaneRuleCompiler.Compile([
            Route("local", "web.example.com", "/web", clusterId: "c-web"),
            Route("tun", "api.example.com", "/api", connectionId: "tun-1", clusterId: "c-api"),
        ]);

        Assert.Equal(new[] { "tun", "local" }, Ids(result));
    }

    // ── 去重 ──

    [Fact]
    public void Compile_SameHostAndPath_KeepsOneAndWarns()
    {
        var result = DataPlaneRuleCompiler.Compile([
            Route("r-b", "API.example.com.", "/v1/*"),
            Route("r-a", "api.example.com", "/v1/*", clusterId: "c2"),
        ]);

        // 域名大小写、末尾点都归一后是同一条规则；留 ID 字典序最小的一条
        Assert.Equal(new[] { "r-a" }, Ids(result));
        Assert.Equal(new[] { "r-b" }, result.Suppressed.Select(r => r.Id));
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("r-a", warning);
        Assert.Contains("匹配条件重复", warning);
    }

    [Fact]
    public void Compile_CatchAllAndEmptyPath_AreTheSameRule()
    {
        var result = DataPlaneRuleCompiler.Compile([
            Route("r1", "api.example.com", ""),
            Route("r2", "api.example.com", "/{**catch-all}"),
        ]);

        Assert.Single(result.Rules);
        Assert.Equal("r1", Ids(result)[0]);
    }

    [Fact]
    public void Compile_DifferentPathsOnSameHost_BothKept()
    {
        var result = DataPlaneRuleCompiler.Compile([
            Route("api", "api.example.com", "/api"),
            Route("web", "api.example.com", "/web"),
        ]);

        Assert.Equal(2, result.Rules.Count);
        Assert.Empty(result.Suppressed);
    }

    [Fact]
    public void Compile_DisabledRoutes_AreNotCompiled()
    {
        var result = DataPlaneRuleCompiler.Compile([
            Route("on", "api.example.com", "/v1"),
            Route("off", "api.example.com", "/v1", enabled: false),
        ]);

        Assert.Equal(new[] { "on" }, Ids(result));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Compile_WithoutHost_StillDedupesByPath()
    {
        var result = DataPlaneRuleCompiler.Compile([
            Route("r1", "", "/{**catch-all}"),
            Route("r2", "*", "/"),
        ]);

        // 两条都是「不限域名」，但路径不同：任意路径 vs "/"，不是同一条规则
        Assert.Equal(2, result.Rules.Count);

        var clash = DataPlaneRuleCompiler.Compile([
            Route("r1", "", "/x"),
            Route("r2", null, "/x"),
        ]);
        Assert.Single(clash.Rules);
    }

    // ── 确定性 ──

    [Fact]
    public void Compile_IsDeterministic_RegardlessOfInputOrder()
    {
        var routes = new[]
        {
            Route("r3", "c.example.com", "/v1"),
            Route("r1", "a.example.com", "/v2"),
            Route("r2", "a.example.com", "/v1"),
        };

        var forward = Ids(DataPlaneRuleCompiler.Compile(routes));
        var reversed = Ids(DataPlaneRuleCompiler.Compile(routes.Reverse().ToArray()));

        Assert.Equal(forward, reversed);
        Assert.Equal(new[] { "r2", "r1", "r3" }, forward);
    }

    [Fact]
    public void Compile_NullOrEmpty_ProducesNoRules()
    {
        Assert.Empty(DataPlaneRuleCompiler.Compile(null).Rules);
        Assert.Empty(DataPlaneRuleCompiler.Compile([]).Rules);
    }
}
