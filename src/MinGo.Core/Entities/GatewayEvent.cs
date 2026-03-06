namespace MinGo.Core.Entities;

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
    /// 实例状态变更
    /// </summary>
    InstanceStatusChange = 1,
    /// <summary>
    /// 健康状态变更
    /// </summary>
    HealthStatusChange = 2,
    /// <summary>
    /// 错误告警
    /// </summary>
    ErrorAlert = 3,
    /// <summary>
    /// 性能告警
    /// </summary>
    PerformanceAlert = 4,
    /// <summary>
    /// 安全告警
    /// </summary>
    SecurityAlert = 5
}

/// <summary>
/// Gateway事件
/// </summary>
public class GatewayEvent
{
    /// <summary>
    /// 事件ID
    /// </summary>
    public string EventId { get; set; } = string.Empty;
    /// <summary>
    /// 事件类型
    /// </summary>
    public GatewayEventType EventType { get; set; }
    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTimeOffset EventTime { get; set; }
    /// <summary>
    /// 事件来源
    /// </summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>
    /// 事件数据（JSON格式）
    /// </summary>
    public string EventDataJson { get; set; } = "{}";
    /// <summary>
    /// 事件优先级
    /// </summary>
    public int Priority { get; set; } = 0;
    /// <summary>
    /// 是否已处理
    /// </summary>
    public bool IsProcessed { get; set; } = false;
    /// <summary>
    /// 处理时间
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }
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
    /// 实例ID
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
    /// <summary>
    /// 响应状态
    /// </summary>
    public bool Success { get; set; } = true;
    /// <summary>
    /// 响应消息
    /// </summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>
    /// 响应时间
    /// </summary>
    public DateTimeOffset ResponseTime { get; set; }
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
    /// 订阅的事件类型
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