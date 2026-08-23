using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MinGo.DataPlane.Grpc;
using Yarp.ReverseProxy.Configuration;
using YarpRouteConfig = Yarp.ReverseProxy.Configuration.RouteConfig;
using YarpClusterConfig = Yarp.ReverseProxy.Configuration.ClusterConfig;
using YarpDestinationConfig = Yarp.ReverseProxy.Configuration.DestinationConfig;

namespace MinGo.DataPlane.ConfigSync;

/// <summary>
/// 数据面配置适配器 - 负责 gRPC ConfigSnapshot 到 YARP Routes/Clusters 的转换与校验
/// 配置基础设施（ChangeToken、原子交换）委托给 InMemoryConfigProvider
/// </summary>
public class DataPlaneConfigProvider
{
    private static readonly HashSet<string> AllowedLoadBalancingPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        "RoundRobin", "LeastRequests", "PowerOfTwoChoices", "FirstAlphabetical", "Random"
    };

    private readonly Lazy<InMemoryConfigProvider> _provider;
    private readonly ILogger<DataPlaneConfigProvider> _logger;
    private int _currentVersion;
    private volatile bool _lastApplySucceeded;

    public DataPlaneConfigProvider(
        IServiceProvider services,
        ILogger<DataPlaneConfigProvider> logger)
    {
        _provider = new Lazy<InMemoryConfigProvider>(() =>
            services.GetRequiredKeyedService<InMemoryConfigProvider>("DataPlane"));
        _logger = logger;
    }

    public int CurrentVersion => _currentVersion;

    public bool LastApplySucceeded => _lastApplySucceeded;

    public IProxyConfig GetConfig() => _provider.Value.GetConfig();

    /// <summary>
    /// 应用来自控制面的配置快照
    /// </summary>
    public void ApplyConfig(ConfigSnapshot snapshot)
    {
        // 版本单调递增检查
        if (snapshot.Version <= _currentVersion)
        {
            _logger.LogDebug("Discarding config snapshot version {Version} (current: {CurrentVersion})", snapshot.Version, _currentVersion);
            return;
        }

        // 预检校验
        var (isValid, errors) = ValidateSnapshot(snapshot);
        if (!isValid)
        {
            _logger.LogWarning("Config snapshot version {Version} failed validation ({ErrorCount} errors): {Errors}",
                snapshot.Version, errors.Count, string.Join("; ", errors));
            return;
        }

        var (routes, clusters) = TranslateConfig(snapshot);
        _provider.Value.Update(routes, clusters);
        _currentVersion = snapshot.Version;
        _lastApplySucceeded = true;
        _logger.LogInformation("Config updated to version {Version} ({UpdateType}, {RouteCount} routes, {ClusterCount} clusters)",
            snapshot.Version, snapshot.UpdateType, routes.Count, clusters.Count);
    }

    private (List<YarpRouteConfig> Routes, List<YarpClusterConfig> Clusters) TranslateConfig(ConfigSnapshot snapshot)
    {
        var routes = new List<YarpRouteConfig>();
        var clusters = new List<YarpClusterConfig>();

        foreach (var route in snapshot.Routes)
        {
            if (!route.Enabled) continue;

            var match = new Yarp.ReverseProxy.Configuration.RouteMatch
            {
                Path = string.IsNullOrEmpty(route.MatchPath) ? null : route.MatchPath,
                Hosts = string.IsNullOrEmpty(route.MatchHost) ? null : new[] { route.MatchHost }
            };

            // 反序列化 transforms
            List<Dictionary<string, string>>? transforms = null;
            if (!string.IsNullOrWhiteSpace(route.TransformsJson) && route.TransformsJson is not "[]" and not "{}")
            {
                try { transforms = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(route.TransformsJson); }
                catch (JsonException ex) { _logger.LogWarning(ex, "Failed to deserialize transforms for route {RouteId}", route.Id); }
            }

            routes.Add(new YarpRouteConfig
            {
                RouteId = route.Id,
                ClusterId = route.ClusterId,
                Match = match,
                // Transforms = transforms
            });
        }

        foreach (var cluster in snapshot.Clusters)
        {
            var destinations = new Dictionary<string, YarpDestinationConfig>();
            foreach (var dest in cluster.Destinations)
            {
                destinations[dest.Id] = new YarpDestinationConfig
                {
                    Address = dest.Address
                };
            }

            clusters.Add(new YarpClusterConfig
            {
                ClusterId = cluster.Id,
                // LoadBalancingPolicy = string.IsNullOrEmpty(cluster.LoadBalancingPolicy) ? "RoundRobin" : cluster.LoadBalancingPolicy,
                Destinations = destinations
            });
        }

        return (routes, clusters);
    }

    /// <summary>
    /// 校验 ConfigSnapshot 的合法性，仅检查会导致 YARP 崩溃的致命错误
    /// </summary>
    internal static (bool IsValid, List<string> Errors) ValidateSnapshot(ConfigSnapshot snapshot)
    {
        var errors = new List<string>();

        // 收集所有 ClusterId 用于引用完整性检查
        var clusterIds = new HashSet<string>(snapshot.Clusters.Select(c => c.Id));

        // 校验 Clusters
        foreach (var cluster in snapshot.Clusters)
        {
            if (string.IsNullOrEmpty(cluster.Id))
            {
                errors.Add("Cluster has empty Id");
            }

            // 校验 LoadBalancingPolicy
            if (!string.IsNullOrEmpty(cluster.LoadBalancingPolicy)
                && !AllowedLoadBalancingPolicies.Contains(cluster.LoadBalancingPolicy))
            {
                errors.Add($"Cluster '{cluster.Id}' has invalid LoadBalancingPolicy '{cluster.LoadBalancingPolicy}'");
            }

            // 校验 Destinations
            foreach (var dest in cluster.Destinations)
            {
                if (string.IsNullOrEmpty(dest.Id))
                {
                    errors.Add($"Cluster '{cluster.Id}' has destination with empty Id");
                }

                if (string.IsNullOrEmpty(dest.Address) || !Uri.TryCreate(dest.Address, UriKind.Absolute, out _))
                {
                    errors.Add($"Cluster '{cluster.Id}' destination '{dest.Id}' has invalid Address '{dest.Address}'");
                }
            }
        }

        // 校验 Routes
        var seenRouteIds = new HashSet<string>();
        foreach (var route in snapshot.Routes)
        {
            if (string.IsNullOrEmpty(route.Id))
            {
                errors.Add("Route has empty Id");
            }
            else if (!seenRouteIds.Add(route.Id))
            {
                errors.Add($"Duplicate RouteId '{route.Id}'");
            }

            if (string.IsNullOrEmpty(route.ClusterId))
            {
                errors.Add($"Route '{route.Id}' has empty ClusterId");
            }
            else if (!clusterIds.Contains(route.ClusterId))
            {
                errors.Add($"Route '{route.Id}' references non-existent Cluster '{route.ClusterId}'");
            }
        }

        return (errors.Count == 0, errors);
    }

    /// <summary>
    /// 获取当前配置中的证书数据
    /// </summary>
    private volatile IReadOnlyList<CertificateData> _certificates = Array.Empty<CertificateData>();
    public IReadOnlyList<CertificateData> CurrentCertificates => _certificates;

    public void UpdateCertificates(IReadOnlyList<CertificateData> certificates)
    {
        _certificates = certificates;
    }

    /// <summary>
    /// 获取当前运行时配置的快照 JSON（用于 CONFIG_REPORT）
    /// </summary>
    public string GetConfigSnapshotJson()
    {
        var config = _provider.Value.GetConfig();
        var snapshot = new ConfigSnapshotDto
        {
            Version = _currentVersion,
            Routes = config.Routes.Select(r => new RouteSnapshotDto
            {
                RouteId = r.RouteId,
                ClusterId = r.ClusterId,
                Match = r.Match == null ? null : new RouteMatchSnapshotDto
                {
                    Path = r.Match.Path,
                    Hosts = r.Match.Hosts?.ToList()
                }
            }).ToList(),
            Clusters = config.Clusters.Select(c => new ClusterSnapshotDto
            {
                ClusterId = c.ClusterId,
                LoadBalancingPolicy = c.LoadBalancingPolicy ?? "RoundRobin",
                Destinations = c.Destinations?.Select(d => new DestinationSnapshotDto
                {
                    Id = d.Key,
                    Address = d.Value?.Address,
                    Healthy = true
                }).ToList() ?? new()
            }).ToList()
        };

        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}

internal class ConfigSnapshotDto
{
    public int Version { get; set; }
    public DateTime ChangeTime { get; set; }
    public List<RouteSnapshotDto> Routes { get; set; } = new();
    public List<ClusterSnapshotDto> Clusters { get; set; } = new();
}

internal class RouteSnapshotDto
{
    public string? RouteId { get; set; }
    public string? ClusterId { get; set; }
    public RouteMatchSnapshotDto? Match { get; set; }
    public bool? Enabled { get; set; }
}

internal class RouteMatchSnapshotDto
{
    public string? Path { get; set; }
    public List<string>? Hosts { get; set; }
}

internal class ClusterSnapshotDto
{
    public string? ClusterId { get; set; }
    public string? LoadBalancingPolicy { get; set; }
    public List<DestinationSnapshotDto> Destinations { get; set; } = new();
}

internal class DestinationSnapshotDto
{
    public string? Id { get; set; }
    public string? Address { get; set; }
    public bool Healthy { get; set; } = true;
}

public static class DataPlaneConfigProviderExtensions
{
    public static IReverseProxyBuilder LoadFromDataPlaneProvider(this IReverseProxyBuilder builder)
    {
        builder.Services.AddKeyedSingleton<InMemoryConfigProvider>("DataPlane", (_, _) =>
            new InMemoryConfigProvider(
                Array.Empty<YarpRouteConfig>(),
                Array.Empty<YarpClusterConfig>()));
        builder.Services.AddSingleton<IProxyConfigProvider>(sp =>
            sp.GetRequiredKeyedService<InMemoryConfigProvider>("DataPlane"));
        builder.Services.AddSingleton<DataPlaneConfigProvider>();
        return builder;
    }
}
