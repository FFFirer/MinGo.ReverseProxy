using Microsoft.Extensions.Primitives;
using MinGo.DataPlane.Grpc;
using Yarp.ReverseProxy.Configuration;
using YarpRouteConfig = Yarp.ReverseProxy.Configuration.RouteConfig;
using YarpClusterConfig = Yarp.ReverseProxy.Configuration.ClusterConfig;
using YarpDestinationConfig = Yarp.ReverseProxy.Configuration.DestinationConfig;

namespace MinGo.DataPlane.ConfigSync;

/// <summary>
/// 数据面配置提供程序 - 从 gRPC ConfigSnapshot 构建 YARP 配置
/// 不直接访问数据库
/// </summary>
public class DataPlaneConfigProvider : IProxyConfigProvider
{
    private volatile DataPlaneProxyConfig _config;
    private readonly object _lock = new();
    private int _currentVersion;

    public DataPlaneConfigProvider()
    {
        _config = new DataPlaneProxyConfig(
            Array.Empty<YarpRouteConfig>(),
            Array.Empty<YarpClusterConfig>(),
            DateTime.UtcNow);
    }

    public int CurrentVersion => _currentVersion;

    public IProxyConfig GetConfig() => _config;

    /// <summary>
    /// 应用来自控制面的配置快照
    /// </summary>
    public void ApplyConfig(ConfigSnapshot snapshot)
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

            routes.Add(new YarpRouteConfig
            {
                RouteId = route.Id,
                ClusterId = route.ClusterId,
                Match = match
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
                LoadBalancingPolicy = string.IsNullOrEmpty(cluster.LoadBalancingPolicy) ? "RoundRobin" : cluster.LoadBalancingPolicy,
                Destinations = destinations
            });
        }

        lock (_lock)
        {
            var oldConfig = _config;
            _config = new DataPlaneProxyConfig(routes, clusters, DateTime.UtcNow);
            oldConfig.SignalChange();
            _currentVersion = snapshot.Version;
        }
    }

    /// <summary>
    /// 获取当前配置中的证书数据
    /// </summary>
    public IReadOnlyList<CertificateData> CurrentCertificates { get; private set; } = Array.Empty<CertificateData>();

    public void UpdateCertificates(IReadOnlyList<CertificateData> certificates)
    {
        CurrentCertificates = certificates;
    }
}

internal class DataPlaneProxyConfig : IProxyConfig
{
    private CancellationTokenSource _cts = new();
    private bool _disposed;

    public DataPlaneProxyConfig(
        IReadOnlyList<YarpRouteConfig> routes,
        IReadOnlyList<YarpClusterConfig> clusters,
        DateTime changeTime)
    {
        Routes = routes;
        Clusters = clusters;
        ChangeTime = changeTime;
        ChangeToken = new CancellationChangeToken(_cts.Token);
    }

    public IReadOnlyList<YarpRouteConfig> Routes { get; }
    public IReadOnlyList<YarpClusterConfig> Clusters { get; }
    public DateTime ChangeTime { get; }
    public IChangeToken ChangeToken { get; private set; }

    public void SignalChange()
    {
        var previousCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        ChangeToken = new CancellationChangeToken(_cts.Token);
        previousCts?.Cancel();
        previousCts?.Dispose();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _disposed = true;
        }
    }
}

public static class DataPlaneConfigProviderExtensions
{
    public static IReverseProxyBuilder LoadFromDataPlaneProvider(this IReverseProxyBuilder builder)
    {
        builder.Services.AddSingleton<DataPlaneConfigProvider>();
        builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DataPlaneConfigProvider>());
        return builder;
    }
}
