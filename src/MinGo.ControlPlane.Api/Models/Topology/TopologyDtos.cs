namespace MinGo.ControlPlane.Api.Models.Topology;

/// <summary>
/// 节点类别，前端按类别决定所在列（连接 → 路由 → 集群 → 端点）。
/// </summary>
public static class TopologyNodeKind
{
    public const string Connection = "connection";
    public const string Route = "route";
    public const string Cluster = "cluster";
    public const string Destination = "destination";
}

/// <summary>
/// 节点着色状态。一律用字符串常量而非枚举：控制面未启用枚举字符串化，
/// 枚举会以数字上线，前端拿到的就是 0/1/2 这类无法直接映射的值。
/// </summary>
public static class TopologyNodeStatus
{
    /// <summary>配置完整且健康。</summary>
    public const string Ok = "ok";

    /// <summary>存在需要关注但不阻断流量的问题。</summary>
    public const string Warning = "warning";

    /// <summary>存在会让请求失败或不可达的问题。</summary>
    public const string Danger = "danger";

    /// <summary>路由已停用，不参与一致性判定。</summary>
    public const string Disabled = "disabled";
}

/// <summary>问题严重级别：info 只进清单，不给节点着色。</summary>
public static class TopologySeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Danger = "danger";
}

/// <summary>
/// 详情面板的一条键值。用有序数组而不是字典，避免依赖字典的枚举顺序。
/// </summary>
public sealed record TopologyDetailItem(string Label, string? Value);

/// <summary>拓扑节点。</summary>
/// <param name="Id">形如 conn:local / route:{id} / cluster:{id} / dest:{clusterId}:{destId}。</param>
/// <param name="Kind"><see cref="TopologyNodeKind"/> 之一。</param>
/// <param name="Label">主标题。</param>
/// <param name="SubLabel">副标题（匹配条件、端点统计、地址等）。</param>
/// <param name="Status"><see cref="TopologyNodeStatus"/> 之一，由该节点命中的问题严重级别推出。</param>
/// <param name="Details">详情面板键值，顺序即展示顺序。</param>
/// <param name="IssueCodes">命中的问题 code，顺序即命中顺序。</param>
public sealed record TopologyNode(
    string Id,
    string Kind,
    string Label,
    string? SubLabel,
    string Status,
    IReadOnlyList<TopologyDetailItem> Details,
    IReadOnlyList<string> IssueCodes);

/// <summary>有向边，方向即流量方向：Source 在前，Target 在后。</summary>
public sealed record TopologyEdge(string Source, string Target, string Kind);

/// <summary>配置一致性问题。NodeIds 是需要着色的节点，清单点击时用于定位。</summary>
public sealed record TopologyIssue(
    string Code,
    string Severity,
    string Message,
    IReadOnlyList<string> NodeIds);

/// <summary>摘要计数，供页面头部指标使用。</summary>
public sealed record TopologySummary(
    int ConnectionCount,
    int RouteCount,
    int EnabledRouteCount,
    int ClusterCount,
    int DestinationCount,
    int UnhealthyDestinationCount,
    int IssueCount);

/// <summary>
/// GET /api/topology 响应：整张图和一致性体检结果由控制面一次算好，前端只做布局与着色。
/// </summary>
public sealed record TopologyResponse(
    DateTimeOffset GeneratedAt,
    TopologySummary Summary,
    IReadOnlyList<TopologyNode> Nodes,
    IReadOnlyList<TopologyEdge> Edges,
    IReadOnlyList<TopologyIssue> Issues);
