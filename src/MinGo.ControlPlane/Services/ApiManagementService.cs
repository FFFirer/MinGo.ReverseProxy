using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Services;

public class ApiManagementService : IApiManagementService
{
    private readonly ILogger<ApiManagementService> _logger;
    private readonly Dictionary<string, MinGo.Shared.Models.RouteConfig> _routes;
    private readonly Dictionary<string, ClusterConfig> _clusters;

    public ApiManagementService(ILogger<ApiManagementService> logger)
    {
        _logger = logger;
        _routes = new Dictionary<string, MinGo.Shared.Models.RouteConfig>();
        _clusters = new Dictionary<string, ClusterConfig>();
        
        InitializeSampleData();
    }

    public Task<IEnumerable<MinGo.Shared.Models.RouteConfig>> GetRoutesAsync()
    {
        return Task.FromResult<IEnumerable<MinGo.Shared.Models.RouteConfig>>(_routes.Values);
    }

    public Task<MinGo.Shared.Models.RouteConfig> GetRouteAsync(string id)
    {
        _routes.TryGetValue(id, out var route);
        return Task.FromResult(route ?? new MinGo.Shared.Models.RouteConfig());
    }

    public Task<MinGo.Shared.Models.RouteConfig> CreateRouteAsync(MinGo.Shared.Models.RouteConfig route)
    {
        var routeId = Guid.NewGuid().ToString();
        _routes[routeId] = route;
        _logger.LogInformation("Created route: {RouteId}", routeId);
        return Task.FromResult(route);
    }

    public Task<MinGo.Shared.Models.RouteConfig> UpdateRouteAsync(string id, MinGo.Shared.Models.RouteConfig route)
    {
        if (_routes.ContainsKey(id))
        {
            _routes[id] = route;
            _logger.LogInformation("Updated route: {RouteId}", id);
            return Task.FromResult(route);
        }
        
        return Task.FromResult(new MinGo.Shared.Models.RouteConfig());
    }

    public Task DeleteRouteAsync(string id)
    {
        if (_routes.Remove(id))
        {
            _logger.LogInformation("Deleted route: {RouteId}", id);
        }
        return Task.CompletedTask;
    }

    public Task<IEnumerable<ClusterConfig>> GetClustersAsync()
    {
        return Task.FromResult<IEnumerable<ClusterConfig>>(_clusters.Values);
    }

    public Task<ClusterConfig> GetClusterAsync(string id)
    {
        _clusters.TryGetValue(id, out var cluster);
        return Task.FromResult(cluster ?? new ClusterConfig());
    }

    public Task<ClusterConfig> CreateClusterAsync(ClusterConfig cluster)
    {
        var clusterId = Guid.NewGuid().ToString();
        _clusters[clusterId] = cluster;
        _logger.LogInformation("Created cluster: {ClusterId}", clusterId);
        return Task.FromResult(cluster);
    }

    public Task<ClusterConfig> UpdateClusterAsync(string id, ClusterConfig cluster)
    {
        if (_clusters.ContainsKey(id))
        {
            _clusters[id] = cluster;
            _logger.LogInformation("Updated cluster: {ClusterId}", id);
            return Task.FromResult(cluster);
        }
        
        return Task.FromResult(new ClusterConfig());
    }

    public Task DeleteClusterAsync(string id)
    {
        if (_clusters.Remove(id))
        {
            _logger.LogInformation("Deleted cluster: {ClusterId}", id);
        }
        return Task.CompletedTask;
    }

    private void InitializeSampleData()
    {
        _routes["route1"] = new MinGo.Shared.Models.RouteConfig
        {
            ClusterId = "cluster1",
            Match = new RouteMatch { Path = "/api/users/{**catch-all}" },
            Enabled = true
        };

        _routes["route2"] = new MinGo.Shared.Models.RouteConfig
        {
            ClusterId = "cluster2",
            Match = new RouteMatch { Path = "/api/products/{**catch-all}" },
            Enabled = true
        };

        _clusters["cluster1"] = new ClusterConfig
        {
            LoadBalancingPolicy = "RoundRobin",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                { "destination1", new DestinationConfig { Address = "http://localhost:5001", Healthy = true } },
                { "destination2", new DestinationConfig { Address = "http://localhost:5002", Healthy = true } }
            },
            HealthCheck = new HealthCheckConfig
            {
                Active = new HealthCheckActiveConfig
                {
                    Enabled = true,
                    Interval = "00:00:10",
                    Timeout = "00:00:05",
                    Path = "/health"
                }
            }
        };

        _clusters["cluster2"] = new ClusterConfig
        {
            LoadBalancingPolicy = "LeastRequests",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                { "destination1", new DestinationConfig { Address = "http://localhost:6001", Healthy = true } },
                { "destination2", new DestinationConfig { Address = "http://localhost:6002", Healthy = false } }
            },
            HealthCheck = new HealthCheckConfig
            {
                Active = new HealthCheckActiveConfig
                {
                    Enabled = true,
                    Interval = "00:00:10",
                    Timeout = "00:00:05",
                    Path = "/health"
                }
            }
        };
    }
}
