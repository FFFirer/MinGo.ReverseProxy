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
        route.Id = routeId;
        _routes[routeId] = route;
        _logger.LogInformation("Created route: {RouteId}", routeId);
        return Task.FromResult(route);
    }

    public Task<MinGo.Shared.Models.RouteConfig> UpdateRouteAsync(string id, MinGo.Shared.Models.RouteConfig route)
    {
        if (_routes.ContainsKey(id))
        {
            route.Id = id;
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
        cluster.Id = clusterId;
        _clusters[clusterId] = cluster;
        _logger.LogInformation("Created cluster: {ClusterId}", clusterId);
        return Task.FromResult(cluster);
    }

    public Task<ClusterConfig> UpdateClusterAsync(string id, ClusterConfig cluster)
    {
        if (_clusters.ContainsKey(id))
        {
            cluster.Id = id;
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

    public Task<ClusterConfig> AddDestinationAsync(string clusterId, string destinationId, DestinationConfig destination)
    {
        if (_clusters.TryGetValue(clusterId, out var cluster))
        {
            cluster.Destinations[destinationId] = destination;
            _logger.LogInformation("Added destination {DestinationId} to cluster {ClusterId}", destinationId, clusterId);
            return Task.FromResult(cluster);
        }
        return Task.FromResult(new ClusterConfig());
    }

    public Task<ClusterConfig> UpdateDestinationAsync(string clusterId, string destinationId, DestinationConfig destination)
    {
        if (_clusters.TryGetValue(clusterId, out var cluster) && cluster.Destinations.ContainsKey(destinationId))
        {
            cluster.Destinations[destinationId] = destination;
            _logger.LogInformation("Updated destination {DestinationId} in cluster {ClusterId}", destinationId, clusterId);
            return Task.FromResult(cluster);
        }
        return Task.FromResult(new ClusterConfig());
    }

    public Task<ClusterConfig> RemoveDestinationAsync(string clusterId, string destinationId)
    {
        if (_clusters.TryGetValue(clusterId, out var cluster) && cluster.Destinations.Remove(destinationId))
        {
            _logger.LogInformation("Removed destination {DestinationId} from cluster {ClusterId}", destinationId, clusterId);
            return Task.FromResult(cluster);
        }
        return Task.FromResult(new ClusterConfig());
    }

    private void InitializeSampleData()
    {
        _routes["route1"] = new MinGo.Shared.Models.RouteConfig
        {
            Id = "route1",
            Name = "用户服务路由",
            ClusterId = "cluster1",
            Match = new RouteMatch { Path = "/api/users/{**catch-all}" },
            Enabled = true
        };

        _routes["route2"] = new MinGo.Shared.Models.RouteConfig
        {
            Id = "route2",
            Name = "产品服务路由",
            ClusterId = "cluster2",
            Match = new RouteMatch { Path = "/api/products/{**catch-all}" },
            Enabled = true
        };

        _routes["route3"] = new MinGo.Shared.Models.RouteConfig
        {
            Id = "route3",
            Name = "API网关用户服务",
            ClusterId = "cluster1",
            Match = new RouteMatch { Host = "api.example.com", Path = "/api/users/{**catch-all}" },
            Enabled = true
        };

        _routes["route4"] = new MinGo.Shared.Models.RouteConfig
        {
            Id = "route4",
            Name = "管理后台API",
            ClusterId = "cluster2",
            Match = new RouteMatch { Host = "admin.example.com", Path = "/api/{**catch-all}" },
            Enabled = false
        };

        _clusters["cluster1"] = new ClusterConfig
        {
            Id = "cluster1",
            Name = "user-cluster",
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
            Id = "cluster2",
            Name = "product-cluster",
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
