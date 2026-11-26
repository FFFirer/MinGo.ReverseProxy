using System.Text.Json;
using MinGo.Core.Models;

namespace MinGo.Core.Entities;

public class ApiRouteEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>
    /// 路由发布到的接入通道：local 或 Cloudflare 隧道 ID。
    /// </summary>
    public string ConnectionId { get; set; } = ConnectionIds.Local;
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
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 数据面分组：可管理的接入通道资产。路由按 <c>group:&lt;Id&gt;</c> 引用，
/// 只有分配到该分组的实例才会收到对应路由。
/// </summary>
public class DataPlaneGroupEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 实例 → 分组分配表。实例 Id 为主键（一个实例最多一个分组），
/// 落库使分配在控制面重启、实例重连后依然存在（实例注册表本身仍在内存）。
/// </summary>
public class InstanceGroupAssignmentEntity
{
    public string InstanceId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public DataPlaneGroupEntity? Group { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}