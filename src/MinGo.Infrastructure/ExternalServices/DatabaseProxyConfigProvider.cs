using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using MinGo.Core.Models;
using MinGo.Core.Interfaces;
using Yarp.ReverseProxy.Configuration;
using YarpRouteConfig = Yarp.ReverseProxy.Configuration.RouteConfig;
using YarpClusterConfig = Yarp.ReverseProxy.Configuration.ClusterConfig;
using YarpDestinationConfig = Yarp.ReverseProxy.Configuration.DestinationConfig;

namespace MinGo.Infrastructure.ExternalServices;

/// <summary>
/// 数据库配置提供程序
/// 从数据库读取路由和集群配置
/// </summary>
public class DatabaseProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private readonly IApiDbService _apiDbService;
    private readonly ILogger<DatabaseProxyConfigProvider> _logger;
    private volatile DatabaseProxyConfig _config;
    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="apiDbService">API 数据库服务</param>
    /// <param name="logger">日志记录器</param>
    public DatabaseProxyConfigProvider(IApiDbService apiDbService, ILogger<DatabaseProxyConfigProvider> logger)
    {
        _apiDbService = apiDbService;
        _logger = logger;
        _config = LoadConfigFromDatabase();
    }

    /// <summary>
    /// 获取代理配置
    /// </summary>
    /// <returns>代理配置</returns>
    public IProxyConfig GetConfig() => _config;

    /// <summary>
    /// 从数据库加载配置
    /// </summary>
    private DatabaseProxyConfig LoadConfigFromDatabase()
    {
        var routes = new List<YarpRouteConfig>();
        var clusters = new List<YarpClusterConfig>();

        try
        {
            _logger.LogInformation("正在从数据库加载配置");

            // 同步获取路由和集群配置
            var routesTask = _apiDbService.GetRoutesAsync();
            var clustersTask = _apiDbService.GetClustersAsync();
            Task.WaitAll(routesTask, clustersTask);

            var dbRoutes = routesTask.Result;
            var dbClusters = clustersTask.Result;

            // 转换集群配置
            foreach (var cluster in dbClusters)
            {
                var destinations = new Dictionary<string, YarpDestinationConfig>();
                if (cluster.Destinations != null)
                {
                    foreach (var dest in cluster.Destinations)
                    {
                        destinations[dest.Key] = new YarpDestinationConfig
                        {
                            Address = dest.Value.Address
                        };
                    }
                }

                var clusterConfig = new YarpClusterConfig
                {
                    ClusterId = cluster.Id,
                    LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                    Destinations = destinations
                };
                clusters.Add(clusterConfig);
            }

            // 转换路由配置
            foreach (var route in dbRoutes)
            {
                if (route.Enabled)
                {
                    var routeConfig = new YarpRouteConfig
                    {
                        RouteId = route.Id,
                        ClusterId = route.ClusterId,
                        Match = new Yarp.ReverseProxy.Configuration.RouteMatch
                        {
                            Path = route.Match?.Path,
                            Hosts = route.Match?.Host != null ? new[] { route.Match.Host } : Array.Empty<string>()
                        }
                    };
                    routes.Add(routeConfig);
                }
            }

            _logger.LogInformation("成功从数据库加载配置，路由数: {RouteCount}, 集群数: {ClusterCount}", routes.Count, clusters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从数据库加载配置异常");
        }

        _logger.LogDebug("共加载{R}条路由，{C}条集群", routes.Count, clusters.Count);

        return new DatabaseProxyConfig(routes, clusters, DateTime.UtcNow);
    }

    /// <summary>
    /// 手动刷新配置
    /// </summary>
    public void Refresh()
    {
        var latest = LoadConfigFromDatabase();
        var oldConfig = Interlocked.Exchange(ref _config, latest);
        oldConfig.SignalChange();
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// 数据库代理配置
/// </summary>
internal class DatabaseProxyConfig : IProxyConfig
{
    private CancellationTokenSource _cts = new CancellationTokenSource();

    public DatabaseProxyConfig(IReadOnlyList<YarpRouteConfig> routes, IReadOnlyList<YarpClusterConfig> clusters, DateTime changeTime)
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

    // 供 Provider 调用：表示"这份配置已经过期了"
    public void SignalChange()
    {
        var previousCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        ChangeToken = new CancellationChangeToken(_cts.Token);
        previousCts.Cancel();  // 通知 YARP 重新拉配置
    }
}