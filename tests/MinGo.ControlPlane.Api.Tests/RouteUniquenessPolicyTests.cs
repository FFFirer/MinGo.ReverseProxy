using MinGo.ControlPlane.Api.Services;
using MinGo.Core.Models;
using Verdict = MinGo.ControlPlane.Api.Services.RouteUniquenessPolicy.Verdict;

namespace MinGo.ControlPlane.Api.Tests;

/// <summary>
/// 保存期域名去重闸门测试：同一「域名 + 路径」只允许一条启用路由，命中即拒绝保存。
/// 拒绝（而非告警或静默合并）是产品决策——重复键下发到底层就是命中顺序不确定的两条规则。
/// </summary>
public class RouteUniquenessPolicyTests
{
    private static RouteConfig Route(
        string id,
        string? host,
        string? path,
        bool enabled = true,
        string? connectionId = null,
        string? name = null) => new()
    {
        Id = id,
        Name = name ?? id,
        ClusterId = "c1",
        ConnectionId = connectionId ?? ConnectionIds.ForNode(ConnectionIds.DefaultNode),
        Enabled = enabled,
        // RouteMatch.Host/Path 在非空引用下用空串表达「未填」，归一化后等价于任意匹配
        Match = new RouteMatch { Host = host ?? string.Empty, Path = path ?? string.Empty },
    };

    private static Verdict Check(
        RouteConfig candidate,
        RouteConfig[]? existing = null,
        string? updatingId = null)
        => RouteUniquenessPolicy.Check(candidate, existing, updatingId);

    // ── 拦截 ──

    [Fact]
    public void Check_SameHostAndPath_IsRejected()
    {
        var verdict = Check(
            Route("new", "api.example.com", "/v1/*"),
            [Route("old", "api.example.com", "/v1/*")]);

        Assert.False(verdict.IsValid);
        Assert.NotNull(verdict.Error);
    }

    [Theory]
    [InlineData("/{**catch-all)")]   // 右括号写成圆括号：会让 YARP 拒绝整份配置
    [InlineData("/{id")]             // 参数未闭合
    [InlineData("no-leading-slash")]
    public void Check_MalformedPath_IsRejectedRegardlessOfConflicts(string path)
    {
        var verdict = Check(Route("new", "api.example.com", path));

        Assert.False(verdict.IsValid);
        Assert.Contains("语法无效", verdict.Error);
    }

    [Fact]
    public void Check_DisabledRoute_WithMalformedPath_IsAllowedUntilEnabled()
    {
        // 停用路由不进底层配置，允许暂存；启用时会再次经过本校验
        var verdict = Check(Route("new", "api.example.com", "/{**catch-all)", enabled: false));

        Assert.True(verdict.IsValid);
    }

    [Theory]
    [InlineData("API.EXAMPLE.COM", "/v1/*")]
    [InlineData("api.example.com.", "/v1/*")]
    [InlineData("  api.example.com", "/V1/*")]
    public void Check_CaseAndTrailingDotDifferences_AreStillDuplicates(string host, string path)
    {
        // 实测：YARP 域名与路径字面量都是大小写不敏感匹配，所以这些写法在底层就是同一条规则
        var verdict = Check(Route("new", host, path), [Route("old", "api.example.com", "/v1/*")]);

        Assert.False(verdict.IsValid);
    }

    [Fact]
    public void Check_CatchAllAndEmptyPath_AreTheSameRule()
    {
        var verdict = Check(
            Route("new", "api.example.com", "/{**catch-all}"),
            [Route("old", "api.example.com", "")]);

        Assert.False(verdict.IsValid);
    }

    [Fact]
    public void Check_BothWithoutHost_SamePath_IsRejected()
    {
        var verdict = Check(Route("new", null, "/x"), [Route("old", "*", "/x")]);

        Assert.False(verdict.IsValid);
    }

    [Fact]
    public void Check_ErrorNamesOccupantAndItsChannel()
    {
        var verdict = Check(
            Route("new", "api.example.com", "/v1/*", connectionId: "tun-1"),
            [Route("old", "api.example.com", "/v1/*", connectionId: "tun-1", name: "存量 API")]);

        Assert.Contains("存量 API", verdict.Error);
        Assert.Contains("外部隧道", verdict.Error);
        Assert.Contains("域名 “api.example.com”", verdict.Error);
        Assert.Contains("路径 “/v1/*”", verdict.Error);
    }

    [Fact]
    public void Check_NodeOccupant_IsNamedAsNodeChannel()
    {
        var verdict = Check(
            Route("new", "api.example.com", "/v1/*"),
            [Route("old", "api.example.com", "/v1/*", name: "节点路由")]);

        Assert.Contains("数据面节点", verdict.Error);
    }

    // ── 放行 ──

    [Theory]
    [InlineData("api.example.com", "/v2/*", "api.example.com", "/v1/*")]
    [InlineData("other.example.com", "/v1/*", "api.example.com", "/v1/*")]
    [InlineData("*.example.com", "/v1/*", "api.example.com", "/v1/*")]
    [InlineData("api.example.com", "/{**catch-all}", "api.example.com", "/v1/*")]
    public void Check_LegitimateVariations_AreAllowed(
        string newHost, string newPath, string oldHost, string oldPath)
    {
        // 同域名不同路径是网关常态；通配与精确并存是分层设计（精确优先命中）
        var verdict = Check(Route("new", newHost, newPath), [Route("old", oldHost, oldPath)]);

        Assert.True(verdict.IsValid);
        Assert.Null(verdict.Error);
    }

    [Fact]
    public void Check_DisabledOccupant_DoesNotHoldTheKey()
    {
        // 停用路由不进底层配置，允许并存；等它被启用时，那一次保存会走同样的校验
        var verdict = Check(
            Route("new", "api.example.com", "/v1/*"),
            [Route("old", "api.example.com", "/v1/*", enabled: false)]);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void Check_DisabledCandidate_SkipsTheCheck()
    {
        var verdict = Check(
            Route("new", "api.example.com", "/v1/*", enabled: false),
            [Route("old", "api.example.com", "/v1/*")]);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void Check_UpdatingRoute_SkipsItself()
    {
        var verdict = Check(
            Route("old", "api.example.com", "/v1/*"),
            [Route("old", "api.example.com", "/v1/*")],
            updatingId: "old");

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void Check_EnableAfterEdit_BlocksAgainstThirdRoute()
    {
        // 表里已有启用路由 old2 占着键，把停用的 old1 改名改路径并启用时同样要拦
        var verdict = Check(
            Route("old1", "api.example.com", "/v1/*"),
            [
                Route("old1", "api.example.com", "/v0/*", enabled: false),
                Route("old2", "api.example.com", "/v1/*"),
            ],
            updatingId: "old1");

        Assert.False(verdict.IsValid);
        Assert.Contains("old2", verdict.Error);
    }

    [Fact]
    public void Check_EmptyExistingTable_Passes()
    {
        Assert.True(Check(Route("new", "api.example.com", "/v1/*"), []).IsValid);
        Assert.True(Check(Route("new", "api.example.com", "/v1/*")).IsValid);
    }
}
