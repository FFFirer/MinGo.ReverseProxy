using MinGo.Core.Models;

namespace MinGo.Core.Tests.Certificates;

public class RouteCertEnrollmentPolicyTests
{
    private static RouteConfig Route(
        string? host,
        string? connectionId = "node:default",
        bool enabled = true) => new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = "r",
            ClusterId = "c",
            ConnectionId = connectionId!,
            Enabled = enabled,
            Match = new RouteMatch { Path = "/", Host = host },
        };

    [Fact]
    public void CollectHosts_NodeChannel_IsCollected()
    {
        var hosts = RouteCertEnrollmentPolicy.CollectHosts([Route("example.com")]);

        Assert.Equal(new[] { "example.com" }, hosts.Keys);
        Assert.False(hosts["example.com"]);
    }

    [Fact]
    public void CollectHosts_EmptyConnectionId_TreatedAsDataPlane()
    {
        var hosts = RouteCertEnrollmentPolicy.CollectHosts([Route("example.com", connectionId: "")]);

        Assert.Equal(new[] { "example.com" }, hosts.Keys);
    }

    [Fact]
    public void CollectHosts_TunnelChannel_IsExcluded()
    {
        var hosts = RouteCertEnrollmentPolicy.CollectHosts([Route("example.com", connectionId: "tunnel-abc")]);

        Assert.Empty(hosts);
    }

    [Fact]
    public void CollectHosts_DisabledRoute_IsExcluded()
    {
        var hosts = RouteCertEnrollmentPolicy.CollectHosts([Route("example.com", enabled: false)]);

        Assert.Empty(hosts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("  ")]
    public void CollectHosts_AnyHost_IsSkipped(string? host)
    {
        Assert.Empty(RouteCertEnrollmentPolicy.CollectHosts([Route(host)]));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.8")]
    public void CollectHosts_IpLiteral_IsSkipped(string host)
    {
        Assert.Empty(RouteCertEnrollmentPolicy.CollectHosts([Route(host)]));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LocalHost")]
    [InlineData("intranet")]
    public void CollectHosts_LocalhostOrSingleLabel_IsSkipped(string host)
    {
        Assert.Empty(RouteCertEnrollmentPolicy.CollectHosts([Route(host)]));
    }

    [Fact]
    public void CollectHosts_StripsPortAndNormalizesCase()
    {
        var hosts = RouteCertEnrollmentPolicy.CollectHosts([Route("Example.COM:8443")]);

        Assert.Equal(new[] { "example.com" }, hosts.Keys);
    }

    [Fact]
    public void CollectHosts_TrailingDot_IsTrimmed()
    {
        var hosts = RouteCertEnrollmentPolicy.CollectHosts([Route("example.com.")]);

        Assert.Equal(new[] { "example.com" }, hosts.Keys);
    }

    [Fact]
    public void CollectHosts_Wildcard_MapsToBareDomainWithFlag()
    {
        var hosts = RouteCertEnrollmentPolicy.CollectHosts([Route("*.example.com")]);

        Assert.True(hosts.TryGetValue("example.com", out var isWildcard));
        Assert.True(isWildcard);
    }

    [Fact]
    public void CollectHosts_BareAndWildcardSameDomain_WildcardWins()
    {
        var hosts = RouteCertEnrollmentPolicy.CollectHosts(
        [
            Route("example.com"),
            Route("*.example.com"),
        ]);

        Assert.Single(hosts);
        Assert.True(hosts["example.com"]);
    }

    [Fact]
    public void CollectHosts_DuplicateBareDomains_Deduplicated()
    {
        var hosts = RouteCertEnrollmentPolicy.CollectHosts(
        [
            Route("example.com"),
            Route("EXAMPLE.com"),
        ]);

        Assert.Single(hosts);
        Assert.False(hosts["example.com"]);
    }

    [Fact]
    public void CollectHosts_MixedRoutes_OnlyValidDataPlaneDomainsKept()
    {
        var hosts = RouteCertEnrollmentPolicy.CollectHosts(
        [
            Route("api.example.com"),
            Route("tunnel.example.com", connectionId: "tunnel-1"),
            Route("off.example.com", enabled: false),
            Route("127.0.0.1"),
            Route("*.wild.example.com"),
        ]);

        Assert.Equal(new[] { "api.example.com", "wild.example.com" }, hosts.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.True(hosts["wild.example.com"]);
    }
}