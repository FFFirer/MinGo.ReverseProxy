namespace MinGo.Core.Entities;

/// <summary>
/// Gateway实例信息
/// </summary>
public class GatewayInstance
{
    /// <summary>
    /// 实例ID（唯一标识）
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 实例名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 实例版本
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 实例IP地址（兼容旧版）
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// 实例端口（兼容旧版）
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 监听地址列表
    /// </summary>
    public List<string>? ListenerAddresses { get; set; }

    /// <summary>
    /// 实例状态
    /// </summary>
    public GatewayInstanceStatus Status { get; set; } = GatewayInstanceStatus.Unknown;

    /// <summary>
    /// 健康状态
    /// </summary>
    public bool IsHealthy { get; set; } = true;

    /// <summary>
    /// 最后心跳时间
    /// </summary>
    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>
    /// 启动时间
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// 实例元数据（JSON格式）
    /// </summary>
    public string MetadataJson { get; set; } = "{}";

    /// <summary>
    /// CPU使用率
    /// </summary>
    public double CpuUsage { get; set; }

    /// <summary>
    /// 内存使用率
    /// </summary>
    public double MemoryUsage { get; set; }

    /// <summary>
    /// 请求总数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 错误请求数
    /// </summary>
    public long ErrorRequests { get; set; }
}

/// <summary>
/// Gateway实例状态枚举
/// </summary>
public enum GatewayInstanceStatus
{
    /// <summary>
    /// 未知
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 在线
    /// </summary>
    Online = 1,

    /// <summary>
    /// 心跳超时
    /// </summary>
    HeartbeatTimeout = 2,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy = 3,

    /// <summary>
    /// 离线
    /// </summary>
    Offline = 4,

    /// <summary>
    /// 启动中
    /// </summary>
    Starting = 5
}

/// <summary>
/// Gateway实例注册请求
/// </summary>
public class GatewayInstanceRegisterRequest
{
    /// <summary>
    /// 实例ID（可选，如果不提供则自动生成）
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// 实例名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 实例版本
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 实例IP地址（兼容旧版）
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// 实例端口（兼容旧版）
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 监听地址列表
    /// </summary>
    public List<string>? ListenerAddresses { get; set; }

    /// <summary>
    /// 实例元数据
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Gateway实例心跳请求
/// </summary>
public class GatewayInstanceHeartbeatRequest
{
    /// <summary>
    /// 实例ID
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// CPU使用率
    /// </summary>
    public double CpuUsage { get; set; }

    /// <summary>
    /// 内存使用率
    /// </summary>
    public double MemoryUsage { get; set; }

    /// <summary>
    /// 请求总数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 错误请求数
    /// </summary>
    public long ErrorRequests { get; set; }

    /// <summary>
    /// 健康状态
    /// </summary>
    public bool IsHealthy { get; set; } = true;

    /// <summary>
    /// 实例元数据
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Gateway实例响应
/// </summary>
public class GatewayInstanceResponse
{
    /// <summary>
    /// 实例ID
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 实例名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 实例版本
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 实例地址（兼容旧版）
    /// </summary>
    [Obsolete]
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 监听地址列表
    /// </summary>
    public List<string>? ListenerAddresses { get; set; }

    /// <summary>
    /// 实例状态
    /// </summary>
    public GatewayInstanceStatus Status { get; set; }

    /// <summary>
    /// 健康状态
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// 最后心跳时间
    /// </summary>
    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>
    /// 运行时长
    /// </summary>
    public TimeSpan Uptime { get; set; }

    /// <summary>
    /// CPU使用率
    /// </summary>
    public double CpuUsage { get; set; }

    /// <summary>
    /// 内存使用率
    /// </summary>
    public double MemoryUsage { get; set; }

    /// <summary>
    /// 请求总数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 错误请求数
    /// </summary>
    public long ErrorRequests { get; set; }

    /// <summary>
    /// 错误率
    /// </summary>
    public double ErrorRate { get; set; }

    /// <summary>
    /// 实例元数据
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Gateway实例列表响应
/// </summary>
public class GatewayInstanceListResponse
{
    /// <summary>
    /// 实例列表
    /// </summary>
    public List<GatewayInstanceResponse> Instances { get; set; } = new();

    /// <summary>
    /// 总数
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 在线数量
    /// </summary>
    public int OnlineCount { get; set; }

    /// <summary>
    /// 离线数量
    /// </summary>
    public int OfflineCount { get; set; }

    /// <summary>
    /// 健康数量
    /// </summary>
    public int HealthyCount { get; set; }

    /// <summary>
    /// 不健康数量
    /// </summary>
    public int UnhealthyCount { get; set; }
}