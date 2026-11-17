using MinGo.ControlPlane.Api.Services;

namespace MinGo.ControlPlane.Api.Tests;

/// <summary>
/// Cloudflare 隧道 ingress 规则合并器的纯函数测试。
/// </summary>
public class IngressReconcilerTests
{
    private const string Origin = "http://data-plane:8080";

    private static IngressRule Rule(string hostname, string service) =>
        new(hostname, service, null, null);

    // ── NormalizeHost ──

    [Theory]
    [InlineData("  API.Example.COM. ", "api.example.com")]
    [InlineData("api.example.com.", "api.example.com")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizeHost_TrimsLowersAndStripsTrailingDot(string? input, string expected)
    {
        Assert.Equal(expected, IngressReconciler.NormalizeHost(input));
    }

    // ── IsValidHostname ──

    [Theory]
    [InlineData("api.example.com")]
    [InlineData("*.example.com")]
    [InlineData("a-b.example.com")]
    [InlineData("xn--fsq.example.com")]
    public void IsValidHostname_AcceptsWellFormedHosts(string host)
    {
        Assert.True(IngressReconciler.IsValidHostname(host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*.example.com/path")]
    [InlineData("https://api.example.com")]
    [InlineData("api.example.com:8443")]
    [InlineData("*. ")]
    [InlineData("-bad.example.com")]
    [InlineData("bad-.example.com")]
    [InlineData("exa mple.com")]
    public void IsValidHostname_RejectsInvalidHosts(string? host)
    {
        Assert.False(IngressReconciler.IsValidHostname(host));
    }

    // ── Merge ──

    private const string CatchAll = "http_status:404";

    [Fact]
    public void Merge_AddsManagedHostsBeforeCatchAll_AndAppendsDefaultCatchAll()
    {
        var current = new[] { Rule("keep.example.com", "http://other:3000") };
        var managed = new[] { "api.example.com", "API.Example.COM" };

        var result = IngressReconciler.Merge(current, managed, managed, Origin, CatchAll);

        Assert.True(result.Changed);
        // 去重后只添加一条 api.example.com
        Assert.Equal(new[] { "api.example.com" }, result.AddedHosts);
        Assert.Empty(result.RemovedHosts);
        Assert.Equal(Origin, result.Rules.Single(r => r.Hostname == "api.example.com").Service);
        Assert.Null(result.Rules.Single(r => r.Hostname == "api.example.com").Path);
        // 非托管规则保留且位于托管规则之前
        Assert.Equal("http://other:3000", result.Rules.First(r => r.Hostname == "keep.example.com").Service);
        Assert.True(Array.IndexOf(result.Rules, result.Rules.First(r => r.Hostname == "api.example.com"))
                    < Array.IndexOf(result.Rules, result.Rules.Last()));
        // 末尾追加兜底规则
        Assert.Equal(string.Empty, result.Rules[^1].Hostname);
        Assert.Equal(CatchAll, result.Rules[^1].Service);
    }

    [Fact]
    public void Merge_UpdatesManagedRule_WhenServiceOrPathDiffers()
    {
        var current = new[] { Rule("api.example.com", "http://legacy:9000") };
        var managed = new[] { "api.example.com" };

        var result = IngressReconciler.Merge(current, managed, managed, Origin, CatchAll);

        Assert.True(result.Changed);
        var rule = result.Rules.Single(r => r.Hostname == "api.example.com");
        Assert.Equal(Origin, rule.Service);
        Assert.Null(rule.Path);
    }

    [Fact]
    public void Merge_KeepsManagedRuleUnchanged_WhenAlreadyPointingAtOrigin()
    {
        var current = new[]
        {
            Rule("api.example.com", Origin),
            new IngressRule(string.Empty, CatchAll, null, null),
        };
        var managed = new[] { "api.example.com" };

        var result = IngressReconciler.Merge(current, managed, managed, Origin, CatchAll);

        Assert.False(result.Changed);
        Assert.Equal(2, result.Rules.Length);
        Assert.Equal(Origin, result.Rules[0].Service);
    }

    [Fact]
    public void Merge_RemovesStaleManagedRule_WhenHostNoLongerBound()
    {
        var current = new[]
        {
            Rule("old.example.com", Origin),
            Rule("user.example.com", "http://direct:3000"),
        };
        var managed = new[] { "api.example.com" };

        // owned 不含 old.example.com：靠 service == origin 的历史痕迹兜底摘除
        var result = IngressReconciler.Merge(current, managed, managed, Origin, CatchAll);

        Assert.True(result.Changed);
        Assert.Equal(new[] { "old.example.com" }, result.RemovedHosts);
        Assert.DoesNotContain(result.Rules, r => r.Hostname == "old.example.com");
        // 非托管规则不受影响
        Assert.Contains(result.Rules, r => r.Hostname == "user.example.com" && r.Service == "http://direct:3000");
        // 托管清单新增
        Assert.Equal(new[] { "api.example.com" }, result.AddedHosts);
    }

    [Fact]
    public void Merge_RemovesDisabledRouteRule_WhenServiceIsAForeignOrigin()
    {
        // 所有权以本地路由表为准：停用路由的 hostname 即使指向连接器专用地址，也必须摘除，
        // 否则解绑后域名仍在隧道上生效
        var current = new[]
        {
            Rule("old.example.com", "http://192.168.88.134:9180"),
            new IngressRule(string.Empty, CatchAll, null, null),
        };
        var managed = Array.Empty<string>();
        var owned = new[] { "old.example.com" };

        var result = IngressReconciler.Merge(current, managed, owned, Origin, CatchAll);

        Assert.True(result.Changed);
        Assert.Equal(new[] { "old.example.com" }, result.RemovedHosts);
        Assert.DoesNotContain(result.Rules, r => r.Hostname == "old.example.com");
    }

    [Fact]
    public void Merge_RewritesInPlace_WhenOriginChanges_NoDuplicateRule()
    {
        // 换用连接器可达的地址后重对账：同一 hostname 只能留一条，且指向新 origin
        var lanOrigin = "http://192.168.88.134:8080";
        var current = new[]
        {
            Rule("api.example.com", Origin),
            new IngressRule(string.Empty, CatchAll, null, null),
        };
        var managed = new[] { "api.example.com" };

        var result = IngressReconciler.Merge(current, managed, managed, lanOrigin, CatchAll);

        Assert.True(result.Changed);
        Assert.Single(result.Rules, r => r.Hostname == "api.example.com");
        Assert.Equal(lanOrigin, result.Rules.Single(r => r.Hostname == "api.example.com").Service);
        Assert.Empty(result.AddedHosts);
        Assert.Empty(result.RemovedHosts);
    }

    [Fact]
    public void Merge_CollapsesDuplicateRules_ForSameManagedHost()
    {
        var current = new[]
        {
            Rule("api.example.com", Origin),
            Rule("api.example.com", "http://192.168.88.134:9180"),
            new IngressRule(string.Empty, CatchAll, null, null),
        };
        var managed = new[] { "api.example.com" };

        var result = IngressReconciler.Merge(current, managed, managed, Origin, CatchAll);

        Assert.True(result.Changed);
        Assert.Single(result.Rules, r => r.Hostname == "api.example.com");
        Assert.Equal(2, result.Rules.Length);
    }

    [Fact]
    public void Merge_KeepsUserRule_WhenHostMatchesNoOwnedRoute()
    {
        // 用户手写、指向外部地址的域名不受影响
        var current = new[]
        {
            Rule("blog.example.com", "http://192.168.88.134:3001"),
            new IngressRule(string.Empty, CatchAll, null, null),
        };
        var managed = Array.Empty<string>();
        var owned = new[] { "api.example.com" };

        var result = IngressReconciler.Merge(current, managed, owned, Origin, CatchAll);

        Assert.False(result.Changed);
        Assert.Contains(result.Rules, r => r.Hostname == "blog.example.com"
                                           && r.Service == "http://192.168.88.134:3001");
    }

    [Fact]
    public void Merge_PreservesUserCatchAllContent()
    {
        var current = new[] { new IngressRule(string.Empty, "http://fallback:8080", null, null) };
        var managed = new[] { "api.example.com" };

        var result = IngressReconciler.Merge(current, managed, managed, Origin, CatchAll);

        Assert.Contains(result.Rules, r => r.Hostname == string.Empty && r.Service == "http://fallback:8080");
        Assert.Equal(string.Empty, result.Rules[^1].Hostname);
    }

    [Fact]
    public void Merge_DoesNotDuplicateExistingCatchAll()
    {
        var current = new[] { new IngressRule(string.Empty, CatchAll, null, null) };
        var empty = Array.Empty<string>();

        var result = IngressReconciler.Merge(current, empty, empty, Origin, CatchAll);

        Assert.False(result.Changed);
        Assert.Single(result.Rules);
    }

    [Fact]
    public void Merge_AppendsDefaultCatchAll_WhenLastRuleIsNotCatchAll()
    {
        // CF 侧兜底不在末尾时（API 正常不会出现），插入托管规则后仍追加默认兜底收尾
        var current = new[]
        {
            new IngressRule(string.Empty, CatchAll, null, null),
            Rule("user.example.com", "http://direct:3000"),
        };
        var managed = new[] { "api.example.com" };

        var result = IngressReconciler.Merge(current, managed, managed, Origin, CatchAll);

        Assert.Equal(string.Empty, result.Rules[^1].Hostname);
        Assert.Equal(CatchAll, result.Rules[^1].Service);
        Assert.Contains(result.Rules, r => r.Hostname == "api.example.com" && r.Service == Origin);
    }

    [Fact]
    public void Merge_EmptyManagedAndOnlyForeignRules_ReportsNoChange()
    {
        var current = new[]
        {
            Rule("user.example.com", "http://direct:3000"),
            new IngressRule(string.Empty, CatchAll, null, null),
        };
        var empty = Array.Empty<string>();

        var result = IngressReconciler.Merge(current, empty, empty, Origin, CatchAll);

        Assert.False(result.Changed);
        Assert.Equal(current.Length, result.Rules.Length);
    }

    // ── FindZone ──

    [Fact]
    public void FindZone_MatchesExactZone()
    {
        var zones = new[]
        {
            new CloudflareZone("z1", "example.com", "active"),
            new CloudflareZone("z2", "other.com", "active"),
        };

        var zone = IngressReconciler.FindZone(zones, "example.com");

        Assert.Equal("z1", zone?.Id);
    }

    [Fact]
    public void FindZone_PrefersLongestSuffixMatch()
    {
        var zones = new[]
        {
            new CloudflareZone("z1", "example.com", "active"),
            new CloudflareZone("z2", "api.example.com", "active"),
        };

        var zone = IngressReconciler.FindZone(zones, "v1.api.example.com");

        Assert.Equal("z2", zone?.Id);
    }

    [Fact]
    public void FindZone_StripsWildcardPrefixBeforeMatching()
    {
        var zones = new[] { new CloudflareZone("z1", "example.com", "active") };

        var zone = IngressReconciler.FindZone(zones, "*.example.com");

        Assert.Equal("z1", zone?.Id);
    }

    [Fact]
    public void FindZone_ReturnsNull_WhenNoZoneMatches()
    {
        var zones = new[] { new CloudflareZone("z1", "other.com", "active") };

        Assert.Null(IngressReconciler.FindZone(zones, "api.example.com"));
    }

    // ── TunnelCnameTarget ──

    [Fact]
    public void TunnelCnameTarget_FormatsTunnelId()
    {
        Assert.Equal("abc-123.cfargotunnel.com", IngressReconciler.TunnelCnameTarget("abc-123"));
    }
}
