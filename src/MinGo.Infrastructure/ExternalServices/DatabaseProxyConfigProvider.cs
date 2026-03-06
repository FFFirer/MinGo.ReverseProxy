using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using MinGo.Core.Models;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _controlPlaneUrl;
    private readonly ILogger<DatabaseProxyConfigProvider> _logger;
    private volatile DatabaseProxyConfig _config;
    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="httpClientFactory">HTTP 客户端工厂</param>
    /// <param name="controlPlaneUrl">控制平面 URL</param>
    /// <param name="logger">日志记录器</param>
    public DatabaseProxyConfigProvider(IHttpClientFactory httpClientFactory, string controlPlaneUrl, ILogger<DatabaseProxyConfigProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _controlPlaneUrl = controlPlaneUrl;
        _logger = logger;
        _config = LoadConfigFromControlPlane();
    }

    /// <summary>
    /// 获取代理配置
    /// </summary>
    /// <returns>代理配置</returns>
    public IProxyConfig GetConfig() => _config;

    /// <summary>
    /// 从控制平面加载配置
    /// </summary>
    private DatabaseProxyConfig LoadConfigFromControlPlane()
    {
        var routes = new List<YarpRouteConfig>();
        var clusters = new List<YarpClusterConfig>();

        try
        {
            var httpClient = _httpClientFactory.CreateClient("ControlPlane");
            var configUrl = $"{_controlPlaneUrl}/api/ApiManagement/config";
            _logger.LogInformation("正在从 {Url} 拉取配置", configUrl);

            var response = httpClient.GetAsync(configUrl).Result;
            if (response.IsSuccessStatusCode)
            {
                var config = response.Content.ReadFromJsonAsync<ControlPlaneConfigResponse>().Result;
                if (config != null)
                {
                    // 转换集群配置
                    foreach (var cluster in config.Clusters)
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
                    foreach (var route in config.Routes)
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

                    _logger.LogInformation("成功从控制平面拉取配置，路由数: {RouteCount}, 集群数: {ClusterCount}", routes.Count, clusters.Count);
                }
            }
            else
            {
                _logger.LogWarning("拉取配置失败: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "拉取配置异常");
        }

        _logger.LogDebug("共加载{R}条路由，{C}条集群", routes.Count, clusters.Count);

        return new DatabaseProxyConfig(routes, clusters, DateTime.UtcNow);
    }

    /// <summary>
    /// 控制平面配置响应
    /// </summary>
    private class ControlPlaneConfigResponse
    {
        /// <summary>
        /// 路由配置
        /// </summary>
        public List<MinGo.Core.Models.RouteConfig> Routes { get; set; }

        /// <summary>
        /// 集群配置
        /// </summary>
        public List<MinGo.Core.Models.ClusterConfig> Clusters { get; set; }
    }

    /// <summary>
    /// 手动刷新配置
    /// </summary>
    public void Refresh()
    {
        var latest = LoadConfigFromControlPlane();
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