using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using MinGo.Shared.Data;
using MinGo.Shared.Models;
using Yarp.ReverseProxy.Configuration;
using YarpRouteConfig = Yarp.ReverseProxy.Configuration.RouteConfig;
using YarpClusterConfig = Yarp.ReverseProxy.Configuration.ClusterConfig;
using YarpDestinationConfig = Yarp.ReverseProxy.Configuration.DestinationConfig;
using SharedRouteMatch = MinGo.Shared.Models.RouteMatch;
using SharedRouteTransforms = MinGo.Shared.Models.RouteTransforms;

namespace MinGo.Gateway.Services;

/// <summary>
/// 数据库配置提供程序
/// 从数据库读取路由和集群配置
/// </summary>
public class DatabaseProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private readonly ProxyDbContext _dbContext;
    private readonly ILogger<DatabaseProxyConfigProvider> _logger;
    private volatile DatabaseProxyConfig _config;
    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="dbContext">数据库上下文</param>
    /// <param name="logger">日志记录器</param>
    public DatabaseProxyConfigProvider(ProxyDbContext dbContext, ILogger<DatabaseProxyConfigProvider> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _config = new DatabaseProxyConfig(Array.Empty<YarpRouteConfig>(), Array.Empty<YarpClusterConfig>(), DateTime.UtcNow);

        LoadConfigFromDatabase();
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

        var routeEntities = _dbContext.Routes.Where(r => r.Enabled).ToList();
        var clusterEntities = _dbContext.Clusters.Include(c => c.Destinations).ToList();

        foreach (var clusterEntity in clusterEntities)
        {
            var destinations = new Dictionary<string, YarpDestinationConfig>();
            foreach (var dest in clusterEntity.Destinations)
            {
                destinations[dest.Id] = new YarpDestinationConfig
                {
                    Address = dest.Address
                };
            }

            var clusterConfig = new YarpClusterConfig
            {
                ClusterId = clusterEntity.Id,
                LoadBalancingPolicy = clusterEntity.LoadBalancingPolicy,
                Destinations = destinations
            };
            clusters.Add(clusterConfig);
        }

        foreach (var routeEntity in routeEntities)
        {
            var match = routeEntity.GetMatch() ?? new SharedRouteMatch();
            var transforms = routeEntity.GetTransforms() ?? new SharedRouteTransforms();

            var routeConfig = new YarpRouteConfig
            {
                RouteId = routeEntity.Id,
                ClusterId = routeEntity.ClusterId,
                Match = new Yarp.ReverseProxy.Configuration.RouteMatch
                {
                    Path = match.Path,
                    Hosts = string.IsNullOrEmpty(match.Host) ? Array.Empty<string>() : new[] { match.Host }
                }
            };
            routes.Add(routeConfig);
        }

        var oldConfig = _config;
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

    // 供 Provider 调用：表示“这份配置已经过期了”
    public void SignalChange()
    {
        var previousCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        ChangeToken = new CancellationChangeToken(_cts.Token);
        previousCts.Cancel();  // 通知 YARP 重新拉配置
    }
}
