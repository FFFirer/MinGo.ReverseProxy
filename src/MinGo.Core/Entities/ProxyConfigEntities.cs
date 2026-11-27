using System.Text.Json;
using MinGo.Core.Models;

namespace MinGo.Core.Entities;

public class ApiRouteEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>
    /// 路由发布到的接入通道：Cloudflare 隧道 ID，或数据面节点 node:<NodeId>。
    /// </summary>
    public string ConnectionId { get; set; } = ConnectionIds.ForNode(ConnectionIds.DefaultNode);
    public string MatchJson { get; set; } = "{}";
    public string TransformsJson { get; set; } = "[]";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public RouteMatch? GetMatch()
    {
        try
        {
            return JsonSerializer.Deserialize<RouteMatch>(MatchJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public List<Dictionary<string, string>>? GetTransforms()
    {
        if (string.IsNullOrWhiteSpace(TransformsJson) || TransformsJson is "{}" or "[]")
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<Dictionary<string, string>>>(TransformsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public class ApiClusterEntity
{
    public string Id { get; set; } = string.Empty;
    public string LoadBalancingPolicy { get; set; } = "RoundRobin";
    public string HealthCheckJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ApiDestinationEntity> Destinations { get; set; } = new();

    public HealthCheckConfig? GetHealthCheck()
    {
        try
        {
            return JsonSerializer.Deserialize<HealthCheckConfig>(HealthCheckJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public class ApiDestinationEntity
{
    public string Id { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool Healthy { get; set; } = true;
    public string ClusterId { get; set; } = string.Empty;
    public ApiClusterEntity? Cluster { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ApiCertificateEntity
{
    public string Id { get; set; } = string.Empty;
    public string DomainName { get; set; } = string.Empty;
    public string CertificateType { get; set; } = "Pfx";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Subject { get; set; }
    public string? Issuer { get; set; }
    public string Thumbprint { get; set; } = string.Empty;
    public bool IsValid { get; set; } = true;
    public byte[]? CertificateData { get; set; }
    public string? Password { get; set; }

    /// <summary>自动接入导入的来源登记 Id；人工上传为 null。</summary>
    public string? EnrollmentId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 数据面节点：可管理的接入通道资产。路由按 <c>node:&lt;Id&gt;</c> 引用，
/// 只有绑定到该节点的实例才会收到对应路由；未绑定实例不推送配置。
/// </summary>
public class DataPlaneNodeEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 实例 → 节点绑定表。实例 Id 为主键（一个实例最多绑定一个节点），
/// 落库使绑定在控制面重启、实例重连后依然存在（实例注册表本身仍在内存）。
/// </summary>
public class InstanceNodeAssignmentEntity
{
    public string InstanceId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public DataPlaneNodeEntity? Node { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}