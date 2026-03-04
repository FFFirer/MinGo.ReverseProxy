namespace MinGo.Shared.Models;

/// <summary>
/// Gateway事件类型
/// </summary>
public enum GatewayEventType
{
    /// <summary>
    /// 配置更新
    /// </summary>
    ConfigUpdate = 0,
    /// <summary>
    /// 证书更新
    /// </summary>
    CertificateUpdate = 1,
    /// <summary>
    /// 路由更新
    /// </summary>
    RouteUpdate = 2,
    /// <summary>
    /// 集群更新
    /// </summary>
    ClusterUpdate = 3,
    /// <summary>
    /// 实例重启
    /// </summary>
    Restart = 4,
    /// <summary>
    /// 实例下线
    /// </summary>
    Shutdown = 5,
    /// <summary>
    /// 健康检查
    /// </summary>
    HealthCheck = 6,
    /// <summary>
    /// 自定义事件
    /// </summary>
    Custom = 7
}

/// <summary>
/// Gateway事件
/// </summary>
public class GatewayEvent
{
    /// <summary>
    /// 事件ID
    /// </summary>
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 事件类型
    /// </summary>
    public GatewayEventType EventType { get; set; }

    /// <summary>
    /// 事件数据（JSON格式）
    /// </summary>
    public string EventData { get; set; } = "{}";

    /// <summary>
    /// 事件创建时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 事件过期时间
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// 目标实例ID（为空表示所有实例）
    /// </summary>
    public string? TargetInstanceId { get; set; }

    /// <summary>
    /// 事件优先级
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// 事件来源
    /// </summary>
    public string Source { get; set; } = "ControlPlane";
}

/// <summary>
/// Gateway事件响应
/// </summary>
public class GatewayEventResponse
{
    /// <summary>
    /// 事件ID
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 处理状态
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 处理时间
    /// </summary>
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 实例ID
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
}

/// <summary>
/// Gateway事件订阅请求
/// </summary>
public class GatewayEventSubscriptionRequest
{
    /// <summary>
    /// 实例ID
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 订阅的事件类型列表
    /// </summary>
    public List<GatewayEventType> EventTypes { get; set; } = new();

    /// <summary>
    /// 回调URL
    /// </summary>
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>
    /// 订阅过期时间
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
