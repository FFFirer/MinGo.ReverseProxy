using MinGo.Core.Models;
using MinGo.Core.Routing;

namespace MinGo.Core.Tests.Routing;

/// <summary>
/// 底层配置编译器测试。重点锁四条契约：
/// 绑定外部隧道的启用路由必须同样产出数据面规则（隧道不直连后端，对所有已绑定实例生效）、
/// 节点路由只装进目标节点的实例、未绑定节点的实例得到空规则集、同一匹配键只留一条且谁留谁走是确定的。
/// </summary>
public class DataPlaneRuleCompilerTests
{
    private static RouteConfig Route(
        string id,
        string? host,
        string? path,
        bool enabled = true,
        string connectionId = null!,
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

    /// <summary>默认节点通道路由（路由表单里节点通道的典型取值）。</summary>
    private static RouteConfig NodeRoute(
        string id,
        string? host,
        string? path,
        string nodeId = ConnectionIds.DefaultNode,
        bool enabled = true) =>
        Route(id, host, path, enabled, ConnectionIds.ForNode(nodeId));

    /// <summary>Cloudflare 隧道路由。</summary>
    private static RouteConfig TunnelRoute(
        string id,
        string? host,
        string? path,
        bool enabled = true) =>
        Route(id, host, path, enabled, $"tun-{id}");

    private static IReadOnlyList<string> Ids(DataPlaneRuleCompiler.Result result) =>
        result.Rules.Select(r => r.Id).ToArray();

    // ── 隧道不直连后端：隧道路由对所有已绑定节点的实例生效 ──

    [Fact]
    public void Compile_KeepsTunnelBoundRoutes()
    {
        // 契约守卫：接入通道只决定流量从哪里进来。隧道 ingress 一律回源到数据面，
        // 所以绑隧道的路由必须在数据面也有规则，否则隧道流量落地即 404。
        // 谁把这里改成「按 ConnectionId 过滤」，这条先红。
        var result = DataPlaneRuleCompiler.Compile([
            TunnelRoute("t1", "api.example.com", "/api/{**catch-all}"),
        ], ConnectionIds.DefaultNode);

        Assert.Equal(new[] { "t1" }, Ids(result));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Compile_TunnelRoutes_ReachEveryBoundNode()
    {
        var tun = TunnelRoute("tun", "api.example.com", "/api");

        // 隧道路由不按节点过滤：任何已绑定节点的实例都收到
        Assert.Equal(new[] { "tun" }, Ids(DataPlaneRuleCompiler.Compile([tun], "default")));
        Assert.Equal(new[] { "tun" }, Ids(DataPlaneRuleCompiler.Compile([tun], "home")));
    }

    [Fact]
    public void Compile_TunnelAndNodeRoutes_BothProduceRules()
    {
        var result = DataPlaneRuleCompiler.Compile([
            NodeRoute("local", "web.example.com", "/web"),
            TunnelRoute("tun", "api.example.com", "/api"),
        ], ConnectionIds.DefaultNode);

        Assert.Equal(new[] { "tun", "local" }, Ids(result));
    }

    // ── 节点过滤（node:<NodeId> 只装进目标节点的实例） ──

    [Fact]
    public void Compile_NodeRoutes_OnlyForTargetNode()
    {
        var nodeRoute = NodeRoute("g1", "api.example.com", "/api", nodeId: "home");

        // 目标节点匹配 → 收节点路由
        var matched = DataPlaneRuleCompiler.Compile([nodeRoute], "home");
        Assert.Equal(new[] { "g1" }, Ids(matched));

        // 其他节点 → 不收
        var other = DataPlaneRuleCompiler.Compile([nodeRoute], "office");
        Assert.Empty(other.Rules);

        // 未绑定节点（targetNodeId 为 null）→ 空规则集，不推送任何配置
        var unbound = DataPlaneRuleCompiler.Compile([nodeRoute], null);
        Assert.Empty(unbound.Rules);
    }

    [Fact]
    public void Compile_UnboundNode_ProducesNoRules()
    {
        // fail-closed 契约：未绑定节点的实例不生效任何转发规则，
        // 隧道与节点路由一律不装。
        var routes = new[]
        {
            NodeRoute("local", "web.example.com", "/web"),
            TunnelRoute("tun", "api.example.com", "/api"),
            NodeRoute("g", "grp.example.com", "/grp", nodeId: "home"),
        };

        Assert.Empty(DataPlaneRuleCompiler.Compile(routes, null).Rules);
    }

    [Fact]
    public void Compile_NodeAndTunnelRoutes_ReachBoundInstance()
    {
        var routes = new[]
        {
            NodeRoute("local", "web.example.com", "/web"),
            TunnelRoute("tun", "api.example.com", "/api"),
            NodeRoute("g", "grp.example.com", "/grp", nodeId: "home"),
        };

        Assert.Equal(new[] { "tun", "local" }, Ids(DataPlaneRuleCompiler.Compile(routes, "default")));
        Assert.Equal(new[] { "tun", "g" }, Ids(DataPlaneRuleCompiler.Compile(routes, "home")));
        Assert.Equal(new[] { "tun" }, Ids(DataPlaneRuleCompiler.Compile(routes, "office")));
    }

    [Fact]
    public void Compile_SameKeyAcrossNodes_DoesNotSuppressWithinTargetNode()
    {
        // 同一域名+路径分属两个节点：对单个节点编译时不视为重复（各装各的）
        var result = DataPlaneRuleCompiler.Compile([
            NodeRoute("home", "api.example.com", "/api", nodeId: "home"),
            NodeRoute("office", "api.example.com", "/api", nodeId: "office"),
        ], "home");

        Assert.Equal(new[] { "home" }, Ids(result));
        Assert.Empty(result.Warnings);
    }

    // ── 去重 ──

    [Fact]
    public void Compile_SameHostAndPath_KeepsOneAndWarns()
    {
        var result = DataPlaneRuleCompiler.Compile([
            NodeRoute("r-b", "API.example.com.", "/v1/*"),
            NodeRoute("r-a", "api.example.com", "/v1/*"),
        ], ConnectionIds.DefaultNode);

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
            NodeRoute("r1", "api.example.com", ""),
            NodeRoute("r2", "api.example.com", "/{**catch-all}"),
        ], ConnectionIds.DefaultNode);

        Assert.Single(result.Rules);
        Assert.Equal("r1", Ids(result)[0]);
    }

    [Fact]
    public void Compile_DifferentPathsOnSameHost_BothKept()
    {
        var result = DataPlaneRuleCompiler.Compile([
            NodeRoute("api", "api.example.com", "/api"),
            NodeRoute("web", "api.example.com", "/web"),
        ], ConnectionIds.DefaultNode);

        Assert.Equal(2, result.Rules.Count);
        Assert.Empty(result.Suppressed);
    }

    [Fact]
    public void Compile_DisabledRoutes_AreNotCompiled()
    {
        var result = DataPlaneRuleCompiler.Compile([
            NodeRoute("on", "api.example.com", "/v1"),
            NodeRoute("off", "api.example.com", "/v1", enabled: false),
        ], ConnectionIds.DefaultNode);

        Assert.Equal(new[] { "on" }, Ids(result));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Compile_WithoutHost_StillDedupesByPath()
    {
        var result = DataPlaneRuleCompiler.Compile([
            NodeRoute("r1", "", "/{**catch-all}"),
            NodeRoute("r2", "*", "/"),
        ], ConnectionIds.DefaultNode);

        // 两条都是「不限域名」，但路径不同：任意路径 vs "/"，不是同一条规则
        Assert.Equal(2, result.Rules.Count);

        var clash = DataPlaneRuleCompiler.Compile([
            NodeRoute("r1", "", "/x"),
            NodeRoute("r2", null, "/x"),
        ], ConnectionIds.DefaultNode);
        Assert.Single(clash.Rules);
    }

    // ── 确定性 ──

    [Fact]
    public void Compile_IsDeterministic_RegardlessOfInputOrder()
    {
        var routes = new[]
        {
            NodeRoute("r3", "c.example.com", "/v1"),
            NodeRoute("r1", "a.example.com", "/v2"),
            NodeRoute("r2", "a.example.com", "/v1"),
        };

        var forward = Ids(DataPlaneRuleCompiler.Compile(routes, ConnectionIds.DefaultNode));
        var reversed = Ids(DataPlaneRuleCompiler.Compile(routes.Reverse().ToArray(), ConnectionIds.DefaultNode));

        Assert.Equal(forward, reversed);
        Assert.Equal(new[] { "r2", "r1", "r3" }, forward);
    }

    [Fact]
    public void Compile_NullOrEmpty_ProducesNoRules()
    {
        Assert.Empty(DataPlaneRuleCompiler.Compile(null, ConnectionIds.DefaultNode).Rules);
        Assert.Empty(DataPlaneRuleCompiler.Compile([], ConnectionIds.DefaultNode).Rules);
    }
}
