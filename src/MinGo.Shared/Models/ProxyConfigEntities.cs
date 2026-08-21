using System.Text.Json;

namespace MinGo.Shared.Models;

public class ApiRouteEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
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
    public string Name { get; set; } = string.Empty;
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
