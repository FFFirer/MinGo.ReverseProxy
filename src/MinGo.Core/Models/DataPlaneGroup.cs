namespace MinGo.Core.Models;

/// <summary>
/// 数据面分组视图模型：路由通道选择与实例分组双向维护的共用资产。
/// 路由按 <see cref="ConnectionIds.ForGroup"/> 引用 Id，重命名不破坏既有路由。
/// </summary>
public class DataPlaneGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>分配到该分组的实例数量（含离线，来自分配表）。</summary>
    public int InstanceCount { get; set; }

    /// <summary>分配到该分组且当前在线的实例数量（运行时状态，由 API 层填充）。</summary>
    public int OnlineCount { get; set; }

    /// <summary>引用该分组作为接入通道的路由数量。</summary>
    public int RouteCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>实例的分组归属（未分配时无记录，等价于 null）。</summary>
public sealed record InstanceGroupAssignment(string InstanceId, string GroupId, string GroupName);

/// <summary>
/// 分组删除结果：被路由引用时拒绝删除并列出引用路由名（fail-closed，不级联）。
/// </summary>
public sealed record GroupDeleteResult(bool Deleted, IReadOnlyList<string> ReferencingRouteNames);
