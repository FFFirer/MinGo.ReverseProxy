using Microsoft.AspNetCore.Mvc;
using MinGo.Shared.Models;
using Yarp.ReverseProxy.Configuration;

namespace MinGo.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly ILogger<ConfigController> _logger;
    private readonly InMemoryConfigProvider _configProvider;

    public ConfigController(ILogger<ConfigController> logger, InMemoryConfigProvider configProvider)
    {
        _logger = logger;
        _configProvider = configProvider;
    }

    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            Status = "Running",
            Version = "1.0.0",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    [HttpPost("reload")]
    public async Task<IActionResult> ReloadConfig([FromBody] GatewayConfig config)
    {
        try
        {
            _logger.LogInformation("Received config reload request: {ConfigId}, Version: {Version}", config.Id, config.Version);

            // 转换为YARP的配置格式
            var yarpRoutes = new List<Yarp.ReverseProxy.Configuration.RouteConfig>();
            var yarpClusters = new List<Yarp.ReverseProxy.Configuration.ClusterConfig>();

            // 转换路由
            foreach (var route in config.Routes.Values)
            {
                var yarpRoute = new Yarp.ReverseProxy.Configuration.RouteConfig
                {
                    RouteId = route.Id,
                    ClusterId = route.ClusterId,
                    Match = new Yarp.ReverseProxy.Configuration.RouteMatch
                    {
                        Path = route.Match.Path,
                        Hosts = route.Match.Host != null ? new[] { route.Match.Host } : null
                    }
                    // 注意：YARP的RouteConfig没有Enabled属性，需要在匹配逻辑中处理
                };
                yarpRoutes.Add(yarpRoute);
            }

            // 转换集群
            foreach (var cluster in config.Clusters.Values)
            {
                var destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>();
                
                // 添加目标
                foreach (var destination in cluster.Destinations.Values)
                {
                    destinations[destination.Address] = new Yarp.ReverseProxy.Configuration.DestinationConfig
                    {
                        Address = destination.Address
                    };
                }

                var yarpCluster = new Yarp.ReverseProxy.Configuration.ClusterConfig
                {
                    ClusterId = cluster.Id,
                    LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                    Destinations = destinations
                    // 注意：YARP的HealthCheck配置结构可能不同，需要根据实际版本调整
                };
                yarpClusters.Add(yarpCluster);
            }

            // 更新配置
            _configProvider.Update(yarpRoutes.AsReadOnly(), yarpClusters.AsReadOnly());
            _logger.LogInformation("Successfully reloaded config with {RouteCount} routes and {ClusterCount} clusters", yarpRoutes.Count, yarpClusters.Count);

            return Ok(new
            {
                Status = "Success",
                Message = "Config reloaded successfully",
                RouteCount = yarpRoutes.Count,
                ClusterCount = yarpClusters.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload config");
            return StatusCode(500, new
            {
                Status = "Error",
                Message = "Failed to reload config"
            });
        }
    }
}
