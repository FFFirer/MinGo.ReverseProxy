using System.ComponentModel.DataAnnotations;

namespace MinGo.Core.Models;

/// <summary>
/// 接入通道标识。local 为内置的本地数据面（存量全局通道），
/// 其余为外部通道（Cloudflare 隧道 ID）或数据面分组（<c>group:&lt;GroupId&gt;</c>）。
/// </summary>
public static class ConnectionIds
{
    /// <summary>默认节点的固定 Id（迁移种子 + 启动确保，存量 local 路由迁移至此）。</summary>
    public const string DefaultNode = "default";
    public const string NodePrefix = "node:";

    /// <summary>是否为数据面节点通道（按 Id 引用，节点重命名不影响已发布路由）。</summary>
    public static bool IsNode(string? connectionId) =>
        connectionId is not null && connectionId.StartsWith(NodePrefix, StringComparison.Ordinal);

    /// <summary>从节点通道标识中取出节点 Id；非节点通道返回 null。</summary>
    public static string? GetNodeId(string? connectionId) =>
        IsNode(connectionId) ? connectionId![NodePrefix.Length..] : null;

    public static string ForNode(string nodeId) => NodePrefix + nodeId;
}

public class GatewayConfig
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Dictionary<string, RouteConfig> Routes { get; set; } = new();
    public Dictionary<string, ClusterConfig> Clusters { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public MonitoringConfig Monitoring { get; set; } = new();
}

public class RouteConfig
{
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>
    /// 路由发布到的接入通道：Cloudflare 隧道 ID，或数据面节点 <c>node:&lt;NodeId&gt;</c>。
    /// </summary>
    public string ConnectionId { get; set; } = ConnectionIds.ForNode(ConnectionIds.DefaultNode);

    [Required]
    public RouteMatch Match { get; set; } = new();

    public List<Dictionary<string, string>>? Transforms { get; set; }
    public bool Enabled { get; set; } = true;
}

public class RouteMatch
{
    public string Path { get; set; } = string.Empty;
    public string? Host { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

public class ClusterConfig
{
    public string Id { get; set; } = string.Empty;
    public List<DestinationConfig> Destinations { get; set; } = new();

    [StringLength(50)]
    public string LoadBalancingPolicy { get; set; } = "RoundRobin";

    public HealthCheckConfig HealthCheck { get; set; } = new();
}

public class DestinationConfig
{
    public string Id { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool Healthy { get; set; } = true;
}

public class HealthCheckConfig
{
    public HealthCheckActiveConfig Active { get; set; } = new();
    public HealthCheckPassiveConfig Passive { get; set; } = new();
}

public class HealthCheckActiveConfig
{
    public bool Enabled { get; set; } = true;
    public string Interval { get; set; } = "00:00:10";
    public string Timeout { get; set; } = "00:00:05";
    public string Path { get; set; } = "/health";
    public int[] ExpectedStatusCodes { get; set; } = new[] { 200 };
}

public class HealthCheckPassiveConfig
{
    public bool Enabled { get; set; } = true;
    public string Policy { get; set; } = "FailureRate";
    public string ReactivationPeriod { get; set; } = "00:01:00";
}

public class SecurityConfig
{
    public List<ApiKeyConfig> ApiKeys { get; set; } = new();
    public IpRulesConfig IpRules { get; set; } = new();
    public RateLimitConfig RateLimits { get; set; } = new();
}

public class ApiKeyConfig
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public class IpRulesConfig
{
    public List<string> Whitelist { get; set; } = new();
    public List<string> Blacklist { get; set; } = new();
}

public class RateLimitConfig
{
    public RateLimitGlobalConfig Global { get; set; } = new();
    public RateLimitPerApiKeyConfig PerApiKey { get; set; } = new();
}

public class RateLimitGlobalConfig
{
    public int RequestsPerSecond { get; set; } = 1000;
}

public class RateLimitPerApiKeyConfig
{
    public int RequestsPerSecond { get; set; } = 100;
}

public class MonitoringConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 未实现，且不是待办：指标的对外出口是 OTLP，控制面看板由心跳喂。
    /// 配置键保留只为不打破既有配置文件；仓库不引入 Prometheus 依赖。
    /// </summary>
    public string MetricsEndpoint { get; set; } = "/metrics";
    public string LogLevel { get; set; } = "Information";
}

public class CertificateConfig
{
    public string Id { get; set; } = string.Empty;
    public string DomainName { get; set; } = string.Empty;
    public string CertificateType { get; set; } = "Pfx";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Subject { get; set; }
    public string? Issuer { get; set; }
    public string Thumbprint { get; set; } = string.Empty;

    /// <summary>
    /// 上传时刻的判定快照，不是当前状态。
    /// 是否在时效内请按 <see cref="ExpiresAt"/> 实时判断；任何过滤/选择逻辑都不得依赖本字段。
    /// </summary>
    public bool IsValid { get; set; } = true;
    public byte[]? CertificateData { get; set; }
    public string? Password { get; set; }

    /// <summary>自动接入导入的来源登记 Id；人工上传为 null。</summary>
    public string? EnrollmentId { get; set; }
}