namespace MinGo.Core.Models;

/// <summary>
/// 数据面节点视图模型：路由通道选择与实例绑定双向维护的共用资产。
/// 路由按 <see cref="ConnectionIds.ForNode"/> 引用 Id，重命名不破坏既有路由。
/// Id 为 <see cref="ConnectionIds.DefaultNode"/> 的节点是启动默认节点（不可删除）。
/// </summary>
public class DataPlaneNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>绑定到该节点的实例数量（含离线，来自绑定表）。</summary>
    public int InstanceCount { get; set; }

    /// <summary>绑定到该节点且当前在线的实例数量（运行时状态，由 API 层填充）。</summary>
    public int OnlineCount { get; set; }

    /// <summary>引用该节点作为接入通道的路由数量。</summary>
    public int RouteCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>实例的节点绑定（未绑定时无记录，等价于 null）。</summary>
public sealed record InstanceNodeAssignment(string InstanceId, string NodeId, string NodeName);

/// <summary>
/// 节点删除结果：被路由引用时拒绝删除并列出引用路由名（fail-closed，不级联）。
/// </summary>
public sealed record NodeDeleteResult(bool Deleted, IReadOnlyList<string> ReferencingRouteNames);
