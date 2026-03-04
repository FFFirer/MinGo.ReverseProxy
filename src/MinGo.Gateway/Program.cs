using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Microsoft.Extensions.Primitives;

var builder = WebApplication.CreateBuilder(args);

// 配置反向代理，使用内存配置提供程序
var configuration = new ConfigurationManager();
var proxyConfig = new InMemoryConfigProvider();
builder.Services.AddSingleton<IProxyConfigProvider>(proxyConfig);

builder.Services.AddReverseProxy()
    .LoadFromMemory(new List<Yarp.ReverseProxy.Configuration.RouteConfig>(), new List<Yarp.ReverseProxy.Configuration.ClusterConfig>());

builder.Services.AddHealthChecks();
builder.Services.AddControllers();
builder.Services.AddSingleton(proxyConfig);

var app = builder.Build();

app.UseRouting();

app.MapControllers();

app.MapReverseProxy();

app.MapHealthChecks("/health");

app.Run();

/// <summary>
/// 内存配置提供程序
/// </summary>
public class InMemoryConfigProvider : IProxyConfigProvider
{
    private volatile InMemoryConfig _config;

    public InMemoryConfigProvider()
    {
        // 初始化默认配置
        _config = new InMemoryConfig(
            Array.Empty<RouteConfig>(),
            Array.Empty<ClusterConfig>(),
            DateTime.UtcNow);
    }

    /// <summary>
    /// 获取配置
    /// </summary>
    /// <returns>代理配置</returns>
    public IProxyConfig GetConfig() => _config;

    /// <summary>
    /// 更新配置
    /// </summary>
    /// <param name="routes">路由配置</param>
    /// <param name="clusters">集群配置</param>
    public void Update(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
    {
        // 取消之前的令牌
        if (_config is InMemoryConfig oldConfig)
        {
            oldConfig.CancellationTokenSource.Cancel();
        }
        
        // 创建新的配置
        _config = new InMemoryConfig(routes, clusters, DateTime.UtcNow);
    }

    /// <summary>
    /// 内存配置
    /// </summary>
    private class InMemoryConfig : IProxyConfig
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public InMemoryConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters, DateTime changeTime)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeTime = changeTime;
            ChangeToken = new CancellationChangeToken(_cts.Token);
        }

        public IReadOnlyList<RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public DateTime ChangeTime { get; }
        public IChangeToken ChangeToken { get; }

        /// <summary>
        /// 取消令牌源
        /// </summary>
        public CancellationTokenSource CancellationTokenSource => _cts;
    }
}

