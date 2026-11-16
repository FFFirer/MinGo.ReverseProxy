using MinGo.ControlPlane.Api.Models.Topology;
using MinGo.ControlPlane.Api.Services;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Tests;

/// <summary>
/// 拓扑聚合的纯函数测试：连线关系、节点着色与配置一致性体检。
/// </summary>
public class TopologyServiceTests
{
    private static readonly ConnectionSummary Local = new("local", "本地数据面", "local", null);

    private static ConnectionSummary Tunnel(string id = "tun-1", string? status = "healthy") =>
        new(id, "远端隧道", "cloudflare-tunnel", status);

    private static RouteConfig Route(
        string id,
        string clusterId,
        string host = "*",
        string path = "/",
        bool enabled = true,
        string connectionId = "local") => new()
    {
        Id = id,
        Name = id,
        ClusterId = clusterId,
        ConnectionId = connectionId,
        Enabled = enabled,
        Match = new RouteMatch { Host = host, Path = path },
    };

    private static DestinationConfig Dest(string id, string address, bool healthy = true) =>
        new() { Id = id, Address = address, Healthy = healthy };

    private static ClusterConfig Cluster(string id, params DestinationConfig[] destinations) =>
        new() { Id = id, Destinations = destinations.ToList() };

    private static TopologyResponse Build(
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters,
        IReadOnlyList<ConnectionSummary>? connections = null) =>
        TopologyService.Build(routes, clusters, connections ?? [Local], DateTimeOffset.UnixEpoch);

    private static TopologyNode Node(TopologyResponse response, string id) =>
        response.Nodes.Single(n => n.Id == id);

    private static bool HasEdge(TopologyResponse response, string source, string target) =>
        response.Edges.Any(e => e.Source == source && e.Target == target);

    private static TopologyIssue Issue(TopologyResponse response, string code) =>
        response.Issues.Single(i => i.Code == code);

    // ── 连线 ──

    [Fact]
    public void Build_ChainsConnectionRouteClusterDestination()
    {
        var response = Build(
            [Route("r1", "c1", "api.example.com", "/v1/*")],
            [Cluster("c1", Dest("d1", "http://10.0.0.1:8080"))]);

        Assert.Equal(4, response.Nodes.Count);
        Assert.True(HasEdge(response, "conn:local", "route:r1"));
        Assert.True(HasEdge(response, "route:r1", "cluster:c1"));
        Assert.True(HasEdge(response, "cluster:c1", "dest:c1:d1"));
        Assert.Empty(response.Issues);
        Assert.Equal(TopologyNodeStatus.Ok, Node(response, "dest:c1:d1").Status);
    }

    [Fact]
    public void Build_SharesOneClusterNodeAcrossRoutes_AndScopesDestinationsByCluster()
    {
        var response = Build(
            [Route("r1", "c1"), Route("r2", "c1")],
            [Cluster("c1", Dest("d1", "http://a:80")), Cluster("c2", Dest("d1", "http://b:80"))]);

        Assert.Single(response.Nodes, n => n.Id == "cluster:c1");
        Assert.Equal(2, response.Nodes.Count(n => n.Kind == TopologyNodeKind.Destination));
        Assert.Contains("dest:c1:d1", response.Nodes.Select(n => n.Id));
        Assert.Contains("dest:c2:d1", response.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Build_EmptyConnectionId_BelongsToLocalConnection()
    {
        var response = Build(
            [Route("r1", "c1", connectionId: "")],
            [Cluster("c1", Dest("d1", "http://a:80"))]);

        Assert.True(HasEdge(response, "conn:local", "route:r1"));
    }

    // ── 孤儿路由与缺失通道 ──

    [Fact]
    public void Build_OrphanRoute_ReportsDanger_AndDrawsNoForwardEdge()
    {
        var response = Build([Route("r1", "ghost")], []);

        var issue = Issue(response, "orphan-route");
        Assert.Equal(TopologySeverity.Danger, issue.Severity);
        Assert.Equal(new[] { "route:r1" }, issue.NodeIds);
        Assert.Equal(TopologyNodeStatus.Danger, Node(response, "route:r1").Status);
        Assert.DoesNotContain(response.Edges, e => e.Target == "cluster:ghost");
    }

    [Fact]
    public void Build_RouteOnDeletedTunnel_ReportsDangerOnlyForThatRoute()
    {
        var response = Build(
            [Route("r1", "c1", "a.example.com", "/gone/*", connectionId: "tun-gone"),
             Route("r2", "c1", "b.example.com", "/keep/*")],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [Local]);

        var issue = Issue(response, "connection-missing");
        Assert.Equal(TopologySeverity.Danger, issue.Severity);
        Assert.Equal(new[] { "route:r1" }, issue.NodeIds);
        Assert.Equal(TopologyNodeStatus.Danger, Node(response, "route:r1").Status);
        Assert.Equal(TopologyNodeStatus.Ok, Node(response, "route:r2").Status);
    }

    // ── 端点健康 ──

    [Fact]
    public void Build_AllDestinationsUnhealthy_BehindEnabledRoute_ColorsClusterAndDestinations()
    {
        var response = Build(
            [Route("r1", "c1")],
            [Cluster("c1", Dest("d1", "http://a:80", false), Dest("d2", "http://b:80", false))]);

        var issue = Issue(response, "all-destinations-unhealthy");
        Assert.Equal(TopologySeverity.Danger, issue.Severity);
        Assert.Equal(3, issue.NodeIds.Count);
        Assert.Equal(TopologyNodeStatus.Danger, Node(response, "cluster:c1").Status);
        Assert.Equal(TopologyNodeStatus.Danger, Node(response, "dest:c1:d2").Status);
    }

    [Fact]
    public void Build_AllDestinationsUnhealthy_BehindDisabledRoute_StaysWarning()
    {
        var response = Build(
            [Route("r1", "c1", enabled: false)],
            [Cluster("c1", Dest("d1", "http://a:80", false))]);

        Assert.Equal(TopologySeverity.Warning, Issue(response, "all-destinations-unhealthy").Severity);
        Assert.Equal(TopologyNodeStatus.Warning, Node(response, "cluster:c1").Status);
    }

    [Fact]
    public void Build_PartiallyUnhealthy_ReportsWarning_AndOnlyTintsTheBadDestination()
    {
        var response = Build(
            [Route("r1", "c1")],
            [Cluster("c1", Dest("good", "http://a:80"), Dest("bad", "http://b:80", false))]);

        var issue = Issue(response, "some-destinations-unhealthy");
        Assert.Equal(TopologySeverity.Warning, issue.Severity);
        Assert.Equal(TopologyNodeStatus.Warning, Node(response, "dest:c1:bad").Status);
        Assert.Equal(TopologyNodeStatus.Ok, Node(response, "dest:c1:good").Status);
        Assert.Equal("2 端点 · 1 健康", Node(response, "cluster:c1").SubLabel);
    }

    [Fact]
    public void Build_UnreferencedClusterHealthIssue_IsInfoAndKeepsNodeOk()
    {
        var response = Build(
            [Route("other", "c-x")],
            [Cluster("c1", Dest("good", "http://a:80"), Dest("bad", "http://b:80", false))]);

        Assert.Equal(TopologySeverity.Info, Issue(response, "some-destinations-unhealthy").Severity);
        Assert.Equal(TopologyNodeStatus.Ok, Node(response, "cluster:c1").Status);
        Assert.Equal(TopologyNodeStatus.Ok, Node(response, "dest:c1:bad").Status);
    }

    [Fact]
    public void Build_EmptyCluster_DangerWhenReferencedByEnabledRoute()
    {
        var response = Build([Route("r1", "c1")], [Cluster("c1")]);

        Assert.Equal(TopologySeverity.Danger, Issue(response, "empty-cluster").Severity);
        Assert.Equal(TopologyNodeStatus.Danger, Node(response, "cluster:c1").Status);
        Assert.Equal(0, response.Summary.DestinationCount);
    }

    [Fact]
    public void Build_UnusedCluster_ReportsInfo()
    {
        var response = Build(
            [Route("r1", "c1")],
            [Cluster("c1", Dest("d1", "http://a:80")), Cluster("c2", Dest("d2", "http://b:80"))]);

        Assert.Equal(TopologySeverity.Info, Issue(response, "unused-cluster").Severity);
        Assert.Equal(new[] { "cluster:c2" }, Issue(response, "unused-cluster").NodeIds);
        Assert.Equal(TopologyNodeStatus.Ok, Node(response, "cluster:c2").Status);
    }

    // ── 重复匹配 ──

    [Fact]
    public void Build_DuplicateMatchAmongEnabledRoutes_ReportsWarningOnBoth()
    {
        var response = Build(
            [Route("r1", "c1", "API.example.com", "/v1/*"), Route("r2", "c2", "api.example.com", "/v1/*")],
            [Cluster("c1", Dest("d1", "http://a:80")), Cluster("c2", Dest("d2", "http://b:80"))]);

        var issue = Issue(response, "duplicate-match");
        Assert.Equal(TopologySeverity.Warning, issue.Severity);
        Assert.Equal(new[] { "route:r1", "route:r2" }, issue.NodeIds);
    }

    [Fact]
    public void Build_DuplicateMatch_IgnoresDisabledRoutesAndDifferentPaths()
    {
        var response = Build(
            [Route("r1", "c1", "api.example.com", "/v1/*"),
             Route("r2", "c2", "api.example.com", "/v1/*", enabled: false),
             Route("r3", "c3", "api.example.com", "/v2/*")],
            [Cluster("c1", Dest("d1", "http://a:80")),
             Cluster("c2", Dest("d2", "http://b:80")),
             Cluster("c3", Dest("d3", "http://c:80"))]);

        Assert.DoesNotContain(response.Issues, i => i.Code == "duplicate-match");
    }

    // ── 接入通道状态 ──

    [Theory]
    [InlineData("down", TopologySeverity.Danger)]
    [InlineData("degraded", TopologySeverity.Warning)]
    [InlineData("inactive", TopologySeverity.Warning)]
    [InlineData(null, TopologySeverity.Warning)]
    public void Build_TunnelNotHealthy_ReportsOnConnectionNode(string? status, string expectedSeverity)
    {
        var response = Build(
            [Route("r1", "c1", connectionId: "tun-1")],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [Local, Tunnel("tun-1", status)]);

        Assert.Equal(expectedSeverity, Issue(response, "tunnel-down").Severity);
        Assert.Equal(new[] { "conn:tun-1" }, Issue(response, "tunnel-down").NodeIds);
    }

    [Fact]
    public void Build_HealthyTunnel_ReportsNoIssue()
    {
        var response = Build(
            [Route("r1", "c1", connectionId: "tun-1")],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [Local, Tunnel("tun-1", "healthy")]);

        Assert.DoesNotContain(response.Issues, i => i.Code == "tunnel-down");
        Assert.Equal("远端隧道", Node(response, "conn:tun-1").Label);
    }

    // ── 状态与摘要 ──

    [Fact]
    public void Build_DisabledRoute_IsMarkedDisabled_ButStillReported()
    {
        var response = Build([Route("r1", "ghost", enabled: false)], []);

        Assert.Equal(TopologyNodeStatus.Disabled, Node(response, "route:r1").Status);
        Assert.Contains("orphan-route", Node(response, "route:r1").IssueCodes);
    }

    [Fact]
    public void Build_Summary_CountsRoutesClustersAndDestinations()
    {
        var response = Build(
            [Route("r1", "c1"), Route("r2", "c1", enabled: false)],
            [Cluster("c1", Dest("d1", "http://a:80"), Dest("d2", "http://b:80", false))]);

        Assert.Equal(2, response.Summary.RouteCount);
        Assert.Equal(1, response.Summary.EnabledRouteCount);
        Assert.Equal(1, response.Summary.ClusterCount);
        Assert.Equal(2, response.Summary.DestinationCount);
        Assert.Equal(1, response.Summary.UnhealthyDestinationCount);
        Assert.Equal(1, response.Summary.ConnectionCount);
        Assert.Equal(response.Issues.Count, response.Summary.IssueCount);
    }

    [Fact]
    public void Build_Nodes_AreOrderedByLayerThenLabel()
    {
        var response = Build(
            [Route("r1", "c1")],
            [Cluster("c1", Dest("d2", "http://bbb:80"), Dest("d1", "http://aaa:80"))]);

        Assert.Equal(
            new[] { "conn:local", "route:r1", "cluster:c1", "dest:c1:d1", "dest:c1:d2" },
            response.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Build_ClusterDetailsExposePolicyAndReferrers()
    {
        var cluster = Cluster("c1", Dest("d1", "http://a:80"));
        cluster.LoadBalancingPolicy = "LeastRequests";

        var response = Build([Route("web", "c1", "api.example.com", "/v1/*")], [cluster]);

        var node = Node(response, "cluster:c1");
        Assert.Equal("LeastRequests", node.Details.Single(d => d.Label == "负载均衡策略").Value);
        Assert.Equal("web", node.Details.Single(d => d.Label == "引用路由").Value);
        Assert.Equal("api.example.com /v1/*", Node(response, "route:web").SubLabel);
    }
}
