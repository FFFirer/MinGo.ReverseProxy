using MinGo.ControlPlane.Api.Models.Topology;
using MinGo.ControlPlane.Api.Services;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Tests;

/// <summary>
/// 拓扑聚合的纯函数测试：连线关系、节点着色与配置一致性体检。
/// </summary>
public class TopologyServiceTests
{
    private static readonly ConnectionSummary DefaultNode = new(
        ConnectionIds.ForNode(ConnectionIds.DefaultNode), "默认节点", "data-plane-node", null);

    private static ConnectionSummary NodeConnection(string id = "home", string name = "华东") =>
        new(ConnectionIds.ForNode(id), name, "data-plane-node", null);

    private static ConnectionSummary Tunnel(string id = "tun-1", string? status = "healthy") =>
        new(id, "远端隧道", "cloudflare-tunnel", status);

    private static RouteConfig Route(
        string id,
        string clusterId,
        string host = "*",
        string path = "/",
        bool enabled = true,
        string? connectionId = null) => new()
    {
        Id = id,
        Name = id,
        ClusterId = clusterId,
        ConnectionId = connectionId ?? ConnectionIds.ForNode(ConnectionIds.DefaultNode),
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
        TopologyService.Build(routes, clusters, connections ?? [DefaultNode], DateTimeOffset.UnixEpoch);

    private static TopologyNode Node(TopologyResponse response, string id) =>
        response.Nodes.Single(n => n.Id == id);

    private static bool HasEdge(TopologyResponse response, string source, string target) =>
        response.Edges.Any(e => e.Source == source && e.Target == target);

    private static TopologyIssue Issue(TopologyResponse response, string code) =>
        response.Issues.Single(i => i.Code == code);

    private static string? Detail(TopologyNode node, string label) =>
        node.Details.Single(d => d.Label == label).Value;

    // ── 连线 ──

    [Fact]
    public void Build_ChainsConnectionRouteClusterDestination()
    {
        var response = Build(
            [Route("r1", "c1", "api.example.com", "/v1/*")],
            [Cluster("c1", Dest("d1", "http://10.0.0.1:8080"))]);

        Assert.Equal(4, response.Nodes.Count);
        Assert.True(HasEdge(response, ConnectionNode(ConnectionIds.ForNode(ConnectionIds.DefaultNode)), "route:r1"));
        Assert.True(HasEdge(response, "route:r1", "cluster:c1"));
        Assert.True(HasEdge(response, "cluster:c1", "dest:c1:d1"));
        Assert.Empty(response.Issues);
        Assert.Equal(TopologyNodeStatus.Ok, Node(response, "dest:c1:d1").Status);
    }

    private static string ConnectionNode(string connectionId) => $"conn:{connectionId}";

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
    public void Build_EmptyConnectionId_ReportsMissingChannel()
    {
        // 空白通道键不是任何合法通道：不画 ingress 边，而是产出一条危险级问题
        var response = Build(
            [Route("r1", "c1", connectionId: "")],
            [Cluster("c1", Dest("d1", "http://a:80"))]);

        Assert.DoesNotContain(response.Edges, e => e.Target == "route:r1");
        Assert.Equal(TopologySeverity.Danger, Issue(response, "connection-missing").Severity);
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
    public void Build_MalformedPath_ReportsDanger()
    {
        var response = Build(
            [Route("r1", "c1", "api.example.com", "/{**catch-all)")],
            [Cluster("c1", Dest("d1", "http://10.0.0.1:8080"))]);

        var issue = Issue(response, "invalid-path");
        Assert.Equal(TopologySeverity.Danger, issue.Severity);
        Assert.Equal(new[] { "route:r1" }, issue.NodeIds);
        Assert.Equal(TopologyNodeStatus.Danger, Node(response, "route:r1").Status);
    }

    [Fact]
    public void Build_MalformedPath_OnDisabledRoute_IsInfoOnly()
    {
        // 停用路由不进底层配置，只提示不染红
        var response = Build(
            [Route("r1", "c1", "api.example.com", "/{**catch-all)", enabled: false)],
            [Cluster("c1", Dest("d1", "http://10.0.0.1:8080"))]);

        var issue = Issue(response, "invalid-path");
        Assert.Equal(TopologySeverity.Info, issue.Severity);
        Assert.Equal(TopologyNodeStatus.Disabled, Node(response, "route:r1").Status);
    }

    [Fact]
    public void Build_RouteOnDeletedTunnel_ReportsDangerOnlyForThatRoute()
    {
        var response = Build(
            [Route("r1", "c1", "a.example.com", "/gone/*", connectionId: "tun-gone"),
             Route("r2", "c1", "b.example.com", "/keep/*")],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [DefaultNode]);

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
            [DefaultNode, Tunnel("tun-1", status)]);

        Assert.Equal(expectedSeverity, Issue(response, "tunnel-down").Severity);
        Assert.Equal(new[] { "conn:tun-1" }, Issue(response, "tunnel-down").NodeIds);
    }

    [Fact]
    public void Build_HealthyTunnel_ReportsNoIssue()
    {
        var response = Build(
            [Route("r1", "c1", connectionId: "tun-1")],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [DefaultNode, Tunnel("tun-1", "healthy")]);

        Assert.DoesNotContain(response.Issues, i => i.Code == "tunnel-down");
        Assert.Equal("远端隧道", Node(response, "conn:tun-1").Label);
    }

    // ── 隧道一律经数据面转发 ──

    [Fact]
    public void Build_TunnelRoute_IngressIsDirect_NoDataPlaneHop()
    {
        // 拓扑上没有数据面实体节点：隧道路由的 ingress 边从隧道直达路由，
        // 「隧道 → 数据面」的中转在路由节点的「转发路径」文案里说明。
        var response = Build(
            [Route("r1", "c1", connectionId: "tun-1")],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [DefaultNode, Tunnel()]);

        Assert.True(HasEdge(response, "conn:tun-1", "route:r1"));
        Assert.True(HasEdge(response, "route:r1", "cluster:c1"));
        Assert.DoesNotContain(response.Edges, e => e.Kind == "origin");
    }

    [Fact]
    public void Build_TunnelRoutes_ShareIngressPerRoute_EdgesStayDeduped()
    {
        var response = Build(
            [Route("r1", "c1", "a.example.com", "/a/*", connectionId: "tun-1"),
             Route("r2", "c1", "b.example.com", "/b/*", connectionId: "tun-1")],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [DefaultNode, Tunnel()]);

        Assert.Equal(2, response.Edges.Count(e => e.Kind == "ingress"));
        Assert.DoesNotContain(response.Edges, e => e.Kind == "origin");
    }

    [Fact]
    public void Build_TunnelRoute_ExposesForwardPathInDetails()
    {
        var response = Build(
            [Route("r1", "c1", connectionId: "tun-1")],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [DefaultNode, Tunnel()]);

        Assert.Equal("远端隧道 → 数据面 → 集群",
            Detail(Node(response, "route:r1"), "转发路径"));
    }

    [Fact]
    public void Build_NodeRoute_KeepsNodeAsSoleIngress()
    {
        var response = Build(
            [Route("r1", "c1")],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [DefaultNode, Tunnel()]);

        Assert.DoesNotContain(response.Edges, e => e.Kind == "origin");
        Assert.Equal("节点数据面 → 集群", Detail(Node(response, "route:r1"), "转发路径"));
        // 无人使用的隧道仍出现在图上，但不能成为任何路由的入口
        Assert.DoesNotContain(response.Edges, e => e.Source == "conn:tun-1" || e.Target == "conn:tun-1");
    }

    // ── 数据面节点通道 ──

    [Fact]
    public void Build_NodeChannel_IsNotTreatedAsTunnel()
    {
        var response = Build(
            [Route("r1", "c1", connectionId: ConnectionIds.ForNode("home"))],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [DefaultNode, NodeConnection()]);

        var node = Node(response, ConnectionNode(ConnectionIds.ForNode("home")));
        Assert.Equal("数据面节点", node.SubLabel);
        Assert.Equal("data-plane-node", Detail(node, "类型"));
        Assert.DoesNotContain(response.Issues, i => i.Code == "tunnel-down");
        Assert.DoesNotContain(response.Edges, e => e.Kind == "origin");
    }

    [Fact]
    public void Build_NodeRoute_EntersFromItsNodeConnection()
    {
        var response = Build(
            [Route("r1", "c1", connectionId: ConnectionIds.ForNode("home"))],
            [Cluster("c1", Dest("d1", "http://a:80"))],
            [DefaultNode, NodeConnection()]);

        // 节点路由的规则装在绑定该节点的数据面上：ingress 边从节点连接出发
        Assert.True(HasEdge(response, ConnectionNode(ConnectionIds.ForNode("home")), "route:r1"));
        Assert.DoesNotContain(response.Edges,
            e => e.Source == ConnectionNode(ConnectionIds.ForNode(ConnectionIds.DefaultNode)) && e.Target == "route:r1");
        Assert.Equal("节点数据面 → 集群", Detail(Node(response, "route:r1"), "转发路径"));
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
            new[] { ConnectionNode(ConnectionIds.ForNode(ConnectionIds.DefaultNode)), "route:r1", "cluster:c1", "dest:c1:d1", "dest:c1:d2" },
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
