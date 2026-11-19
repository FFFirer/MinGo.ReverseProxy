using MinGo.ControlPlane.Api.Models.Topology;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using MinGo.Core.Routing;

namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// 网络拓扑聚合服务：把接入通道、路由、集群、目标端点拼成一张有向图，
/// 并在同一趟计算里做完配置一致性体检（孤儿路由、空集群、端点不健康、通道缺失等）。
/// 只读，不触碰 Cloudflare 写接口。
/// </summary>
public class TopologyService
{
    private readonly IApiManagementService _apiManagementService;
    private readonly CloudflareSyncService _cloudflareSyncService;

    public TopologyService(
        IApiManagementService apiManagementService,
        CloudflareSyncService cloudflareSyncService)
    {
        _apiManagementService = apiManagementService;
        _cloudflareSyncService = cloudflareSyncService;
    }

    public async Task<TopologyResponse> GetAsync(CancellationToken cancellationToken)
    {
        var routes = await _apiManagementService.GetRoutesAsync();
        var clusters = await _apiManagementService.GetClustersAsync();
        var connections = await _cloudflareSyncService.ListConnectionsAsync(cancellationToken);

        return Build(routes, clusters, connections, DateTimeOffset.UtcNow);
    }

    // ── 纯函数：图与体检结果只由入参决定，便于单测 ──

    /// <summary>节点 ID：接入通道列。</summary>
    public static string ConnectionNodeId(string connectionId) => $"conn:{connectionId}";

    /// <summary>节点 ID：路由列。</summary>
    public static string RouteNodeId(string routeId) => $"route:{routeId}";

    /// <summary>节点 ID：集群列。</summary>
    public static string ClusterNodeId(string clusterId) => $"cluster:{clusterId}";

    /// <summary>节点 ID：端点列。端点 ID 只在集群内唯一，因此带上 clusterId。</summary>
    public static string DestinationNodeId(string clusterId, string destinationId)
        => $"dest:{clusterId}:{destinationId}";

    /// <summary>
    /// 组装拓扑。路由的 connectionId/clusterId 是唯一的外键来源：
    /// 指向不存在的对象时不补占位节点，而是产出一条问题，让缺失本身可见。
    /// </summary>
    public static TopologyResponse Build(
        IEnumerable<RouteConfig> routes,
        IEnumerable<ClusterConfig> clusters,
        IReadOnlyList<ConnectionSummary> connections,
        DateTimeOffset generatedAt)
    {
        var routeList = routes.Where(r => !string.IsNullOrWhiteSpace(r.Id)).ToList();
        var clusterList = DistinctClusters(clusters);
        var connectionList = DistinctConnections(connections);

        var clusterById = clusterList.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var connectionById = connectionList.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

        // 集群被谁引用：区分「被启用路由引用」和「被任意路由引用」，前者才影响流量。
        var routesByCluster = routeList
            .Where(r => !string.IsNullOrWhiteSpace(r.ClusterId))
            .GroupBy(r => r.ClusterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var drafts = new List<NodeDraft>();
        var issues = new List<TopologyIssue>();
        var nodeIssues = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var nodeSeverity = new Dictionary<string, int>(StringComparer.Ordinal);

        void AddIssue(string code, string severity, string message, params string[] nodeIds)
        {
            var ids = nodeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
            var rank = SeverityRank(severity);

            foreach (var id in ids)
            {
                if (!nodeIssues.TryGetValue(id, out var codes))
                {
                    codes = [];
                    nodeIssues[id] = codes;
                }
                if (!codes.Contains(code, StringComparer.Ordinal)) codes.Add(code);

                // info 只进清单，不参与着色，否则休眠配置会把图染花。
                if (rank >= SeverityRank(TopologySeverity.Warning)
                    && (!nodeSeverity.TryGetValue(id, out var current) || rank > current))
                {
                    nodeSeverity[id] = rank;
                }
            }

            issues.Add(new TopologyIssue(code, severity, message, ids));
        }

        // ── 接入通道 ──
        foreach (var connection in connectionList)
        {
            var nodeId = ConnectionNodeId(connection.Id);
            var isTunnel = !ConnectionIds.IsLocal(connection.Id);
            var referencedRoutes = routeList.Count(r => ConnectionKeyOf(r) == connection.Id);

            if (isTunnel && !string.Equals(connection.Status, "healthy", StringComparison.OrdinalIgnoreCase))
            {
                var severity = string.Equals(connection.Status, "down", StringComparison.OrdinalIgnoreCase)
                    ? TopologySeverity.Danger
                    : TopologySeverity.Warning;
                AddIssue("tunnel-down", severity,
                    $"接入通道 “{connection.Name}” 当前状态：{TunnelStatusLabel(connection.Status)}",
                    nodeId);
            }

            drafts.Add(new NodeDraft(nodeId, TopologyNodeKind.Connection, connection.Name,
                isTunnel ? "Cloudflare Tunnel" : "内置数据面", false,
                [
                    new TopologyDetailItem("通道 ID", connection.Id),
                    new TopologyDetailItem("类型", isTunnel ? "cloudflare-tunnel" : "local"),
                    new TopologyDetailItem("隧道状态", isTunnel ? TunnelStatusLabel(connection.Status) : null),
                    new TopologyDetailItem("引用路由", referencedRoutes.ToString()),
                ]));
        }

        // ── 路由 ──
        foreach (var route in routeList)
        {
            var nodeId = RouteNodeId(route.Id);
            var host = MatchHost(route);
            var path = MatchPath(route);
            var connectionKey = ConnectionKeyOf(route);
            var clusterExists = clusterById.ContainsKey(route.ClusterId ?? string.Empty);

            if (!clusterExists)
            {
                AddIssue("orphan-route", TopologySeverity.Danger,
                    $"路由 “{RouteLabel(route)}” 指向不存在的集群 “{route.ClusterId}”，命中的请求无法转发",
                    nodeId);
            }

            if (!RoutePathSyntax.IsValid(path, out var pathError))
            {
                // 停用路由不进底层配置，不会击穿 YARP，但启用前必须先修正
                AddIssue("invalid-path", route.Enabled ? TopologySeverity.Danger : TopologySeverity.Info,
                    route.Enabled
                        ? $"路由 “{RouteLabel(route)}” 的匹配路径 “{path}” 语法无效（{pathError}），数据面会拒绝整份配置导致全部路由 404"
                        : $"路由 “{RouteLabel(route)}” 的匹配路径 “{path}” 语法无效（{pathError}），启用前必须先修正",
                    nodeId);
            }

            if (!ConnectionIds.IsLocal(connectionKey) && !connectionById.ContainsKey(connectionKey))
            {
                AddIssue("connection-missing", TopologySeverity.Danger,
                    $"路由 “{RouteLabel(route)}” 绑定的接入通道 “{connectionKey}” 已不存在",
                    nodeId);
            }

            // 隧道路由不直连后端：ingress 一律回源到本地数据面，再由数据面的同一条规则转成后端集群。
            // 图上必须把这跳中转画出来，否则 "隧道 → 路由 → 集群" 看起来就是隧道自己找到了后端。
            var connectionName = connectionById.TryGetValue(connectionKey, out var conn)
                ? conn.Name
                : connectionKey;
            var isTunnelRoute = !ConnectionIds.IsLocal(connectionKey);
            var forwardPath = isTunnelRoute
                ? $"{connectionName} → 本地数据面 → 集群"
                : "本地数据面 → 集群";

            drafts.Add(new NodeDraft(nodeId, TopologyNodeKind.Route, RouteLabel(route),
                $"{host} {path}", !route.Enabled,
                [
                    new TopologyDetailItem("路由 ID", route.Id),
                    new TopologyDetailItem("匹配域名", host),
                    new TopologyDetailItem("匹配路径", path),
                    new TopologyDetailItem("启用状态", route.Enabled ? "启用" : "停用"),
                    new TopologyDetailItem("接入通道", connectionName),
                    new TopologyDetailItem("转发路径", forwardPath),
                    new TopologyDetailItem("目标集群", string.IsNullOrWhiteSpace(route.ClusterId) ? null : route.ClusterId),
                    new TopologyDetailItem("转换规则", (route.Transforms?.Count ?? 0).ToString()),
                ]));
        }

        // ── 重复匹配：与保存期去重闸门（RouteUniquenessPolicy）同一把尺子，仅统计启用路由 ──
        foreach (var group in routeList.Where(r => r.Enabled)
                     .GroupBy(r => RouteMatchKey.Of(r.Match?.Host, r.Match?.Path))
                     .Where(g => g.Count() > 1))
        {
            var names = string.Join("、", group.Select(RouteLabel));
            AddIssue("duplicate-match", TopologySeverity.Warning,
                $"路由 {names} 的匹配条件重复（{group.Key}），底层只生效一条（按路由 ID 字典序取先者），实际命中不可预期",
                group.Select(r => RouteNodeId(r.Id)).ToArray());
        }

        // ── 集群与端点 ──
        foreach (var cluster in clusterList)
        {
            var clusterNodeId = ClusterNodeId(cluster.Id);
            var destinations = cluster.Destinations ?? new List<DestinationConfig>();
            var healthyCount = destinations.Count(d => d.Healthy);
            var unhealthyIds = destinations
                .Where(d => !d.Healthy)
                .Select(d => DestinationNodeId(cluster.Id, DestinationKey(d)))
                .ToArray();

            var referrers = routesByCluster.TryGetValue(cluster.Id, out var rs)
                ? rs
                : new List<RouteConfig>();
            var hasEnabledReferrer = referrers.Any(r => r.Enabled);
            var referenced = referrers.Count > 0;

            if (destinations.Count == 0)
            {
                AddIssue("empty-cluster",
                    hasEnabledReferrer ? TopologySeverity.Danger : TopologySeverity.Info,
                    hasEnabledReferrer
                        ? $"集群 “{cluster.Id}” 没有目标端点，引用它的路由全部无处可转"
                        : $"集群 “{cluster.Id}” 没有目标端点",
                    clusterNodeId);
            }
            else if (healthyCount == 0)
            {
                AddIssue("all-destinations-unhealthy",
                    hasEnabledReferrer ? TopologySeverity.Danger : TopologySeverity.Warning,
                    $"集群 “{cluster.Id}” 的 {destinations.Count} 个端点均标记为不健康"
                        + (hasEnabledReferrer ? "，引用它的路由当前不可用" : string.Empty),
                    new[] { clusterNodeId }.Concat(unhealthyIds).ToArray());
            }
            else if (healthyCount < destinations.Count)
            {
                AddIssue("some-destinations-unhealthy",
                    hasEnabledReferrer ? TopologySeverity.Warning : TopologySeverity.Info,
                    $"集群 “{cluster.Id}” 有 {destinations.Count - healthyCount}/{destinations.Count} 个端点标记为不健康",
                    new[] { clusterNodeId }.Concat(unhealthyIds).ToArray());
            }

            if (!referenced)
            {
                AddIssue("unused-cluster", TopologySeverity.Info,
                    $"集群 “{cluster.Id}” 未被任何路由引用",
                    clusterNodeId);
            }

            drafts.Add(new NodeDraft(clusterNodeId, TopologyNodeKind.Cluster, cluster.Id,
                $"{destinations.Count} 端点 · {healthyCount} 健康", false,
                [
                    new TopologyDetailItem("集群 ID", cluster.Id),
                    new TopologyDetailItem("负载均衡策略", cluster.LoadBalancingPolicy),
                    new TopologyDetailItem("端点总数", destinations.Count.ToString()),
                    new TopologyDetailItem("健康端点数", healthyCount.ToString()),
                    new TopologyDetailItem("主动健康检查", cluster.HealthCheck?.Active is { Enabled: true } active
                        ? $"每 {active.Interval} 探测 {active.Path}"
                        : "停用"),
                    new TopologyDetailItem("被动健康检查", cluster.HealthCheck?.Passive is { Enabled: true } passive
                        ? passive.Policy
                        : "停用"),
                    new TopologyDetailItem("引用路由", referenced
                        ? string.Join("、", referrers.Select(RouteLabel))
                        : null),
                ]));

            foreach (var destination in destinations)
            {
                var key = DestinationKey(destination);
                drafts.Add(new NodeDraft(DestinationNodeId(cluster.Id, key), TopologyNodeKind.Destination,
                    string.IsNullOrWhiteSpace(destination.Address) ? "(地址为空)" : destination.Address,
                    destination.Healthy ? "配置健康" : "配置不健康", false,
                    [
                        new TopologyDetailItem("端点 ID", key),
                        new TopologyDetailItem("地址", destination.Address),
                        new TopologyDetailItem("配置健康", destination.Healthy ? "健康" : "不健康"),
                        new TopologyDetailItem("所属集群", cluster.Id),
                    ]));
            }
        }

        // ── 连线：只在两端节点都存在时建立 ──
        // 转发规则一律装在数据面，所以 ingress 边恒从 conn:local 出发；
        // 外部通道多一条 origin 边指向数据面，表示 "隧道的下一跳是数据面，不是后端"。
        var edges = new List<TopologyEdge>();
        var hasLocalConnection = connectionById.ContainsKey(ConnectionIds.Local);
        foreach (var route in routeList)
        {
            var routeNodeId = RouteNodeId(route.Id);
            var connectionKey = ConnectionKeyOf(route);
            var isTunnelRoute = !ConnectionIds.IsLocal(connectionKey);

            if (isTunnelRoute && connectionById.ContainsKey(connectionKey))
            {
                if (hasLocalConnection)
                    edges.Add(new TopologyEdge(ConnectionNodeId(connectionKey),
                        ConnectionNodeId(ConnectionIds.Local), "origin"));
            }

            // 数据面节点缺失（老版本通道清单）时退回通道直连，避免路由在图上悬空
            var ingressKey = isTunnelRoute && !hasLocalConnection ? connectionKey : ConnectionIds.Local;
            if (connectionById.ContainsKey(ingressKey))
                edges.Add(new TopologyEdge(ConnectionNodeId(ingressKey), routeNodeId, "ingress"));

            if (!string.IsNullOrWhiteSpace(route.ClusterId) && clusterById.ContainsKey(route.ClusterId))
                edges.Add(new TopologyEdge(routeNodeId, ClusterNodeId(route.ClusterId), "forward"));
        }

        foreach (var cluster in clusterList)
        {
            foreach (var destination in cluster.Destinations ?? new List<DestinationConfig>())
                edges.Add(new TopologyEdge(ClusterNodeId(cluster.Id),
                    DestinationNodeId(cluster.Id, DestinationKey(destination)), "destination"));
        }

        // ── 定稿：状态由命中的问题严重级别推出，停用路由保持灰色 ──
        var nodes = drafts
            .Select(d => new TopologyNode(
                d.Id, d.Kind, d.Label, d.SubLabel,
                StatusOf(d, nodeSeverity),
                d.Details,
                nodeIssues.TryGetValue(d.Id, out var codes) ? codes : []))
            .OrderBy(n => LayerOf(n.Kind))
            .ThenBy(n => n.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

        var allDestinations = clusterList.SelectMany(c => c.Destinations ?? new List<DestinationConfig>());

        return new TopologyResponse(
            generatedAt,
            new TopologySummary(
                connectionList.Count,
                routeList.Count,
                routeList.Count(r => r.Enabled),
                clusterList.Count,
                allDestinations.Count(),
                allDestinations.Count(d => !d.Healthy),
                issues.Count),
            nodes,
            DedupeEdges(edges),
            issues);
    }

    // ── 辅助 ──

    private static List<ClusterConfig> DistinctClusters(IEnumerable<ClusterConfig>? clusters)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ClusterConfig>();
        foreach (var cluster in clusters ?? [])
        {
            if (string.IsNullOrWhiteSpace(cluster.Id) || !seen.Add(cluster.Id)) continue;
            list.Add(cluster);
        }
        return list;
    }

    private static List<ConnectionSummary> DistinctConnections(IReadOnlyList<ConnectionSummary>? connections)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ConnectionSummary>();
        foreach (var connection in connections ?? [])
        {
            if (string.IsNullOrWhiteSpace(connection.Id) || !seen.Add(connection.Id)) continue;
            list.Add(connection);
        }
        return list;
    }

    private static List<TopologyEdge> DedupeEdges(List<TopologyEdge> edges)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<TopologyEdge>();
        foreach (var edge in edges)
        {
            if (!seen.Add($"{edge.Source}->{edge.Target}")) continue;
            list.Add(edge);
        }
        return list;
    }

    /// <summary>路由归属的通道键：空值与 local 归一为同一个节点。</summary>
    private static string ConnectionKeyOf(RouteConfig route)
        => ConnectionIds.IsLocal(route.ConnectionId) ? ConnectionIds.Local : route.ConnectionId!.Trim();

    private static string RouteLabel(RouteConfig route)
        => string.IsNullOrWhiteSpace(route.Name) ? route.Id : route.Name.Trim();

    private static string MatchHost(RouteConfig route)
    {
        var host = route.Match?.Host;
        return string.IsNullOrWhiteSpace(host) ? "*" : host.Trim();
    }

    private static string MatchPath(RouteConfig route)
    {
        var path = route.Match?.Path;
        return string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
    }

    private static string DestinationKey(DestinationConfig destination)
        => string.IsNullOrWhiteSpace(destination.Id) ? "(未命名端点)" : destination.Id.Trim();

    private static string TunnelStatusLabel(string? status) => status switch
    {
        null or "" => "未知",
        "healthy" => "健康",
        "degraded" => "降级",
        "down" => "断连",
        "inactive" => "未启动",
        _ => status,
    };

    private static int SeverityRank(string severity) => severity switch
    {
        TopologySeverity.Danger => 2,
        TopologySeverity.Warning => 1,
        _ => 0,
    };

    private static string StatusOf(NodeDraft draft, Dictionary<string, int> nodeSeverity)
    {
        if (draft.Disabled) return TopologyNodeStatus.Disabled;
        var rank = nodeSeverity.TryGetValue(draft.Id, out var value) ? value : 0;
        return rank switch
        {
            >= 2 => TopologyNodeStatus.Danger,
            >= 1 => TopologyNodeStatus.Warning,
            _ => TopologyNodeStatus.Ok,
        };
    }

    private static int LayerOf(string kind) => kind switch
    {
        TopologyNodeKind.Connection => 0,
        TopologyNodeKind.Route => 1,
        TopologyNodeKind.Cluster => 2,
        TopologyNodeKind.Destination => 3,
        _ => 4,
    };

    private sealed record NodeDraft(
        string Id,
        string Kind,
        string Label,
        string? SubLabel,
        bool Disabled,
        List<TopologyDetailItem> Details);
}
