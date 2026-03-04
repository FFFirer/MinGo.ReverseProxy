using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Data;

/// <summary>
/// 数据库配置服务
/// </summary>
public class DbConfigService : IDbConfigService
{
    private readonly GatewayDbContext _dbContext;
    private readonly ILogger<DbConfigService> _logger;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="dbContext">数据库上下文</param>
    /// <param name="logger">日志记录器</param>
    public DbConfigService(GatewayDbContext dbContext, ILogger<DbConfigService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 获取最新配置
    /// </summary>
    /// <returns>网关配置</returns>
    public async Task<GatewayConfig> GetLatestConfigAsync()
    {
        var entity = await _dbContext.GatewayConfigs
            .Include(g => g.Routes)
            .Include(g => g.Clusters)
            .ThenInclude(c => c.Destinations)
            .OrderByDescending(g => g.CreatedAt)
            .FirstOrDefaultAsync();

        if (entity == null)
        {
            return new GatewayConfig
            {
                Id = Guid.NewGuid().ToString(),
                Version = "1.0.0",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "System",
                Routes = new Dictionary<string, RouteConfig>(),
                Clusters = new Dictionary<string, ClusterConfig>(),
                Security = new SecurityConfig(),
                Monitoring = new MonitoringConfig()
            };
        }

        return MapToModel(entity);
    }

    /// <summary>
    /// 根据版本获取配置
    /// </summary>
    /// <param name="version">版本号</param>
    /// <returns>网关配置</returns>
    public async Task<GatewayConfig> GetConfigByVersionAsync(string version)
    {
        var entity = await _dbContext.GatewayConfigs
            .Include(g => g.Routes)
            .Include(g => g.Clusters)
            .ThenInclude(c => c.Destinations)
            .FirstOrDefaultAsync(g => g.Version == version);

        return entity != null ? MapToModel(entity) : new GatewayConfig();
    }

    /// <summary>
    /// 创建配置
    /// </summary>
    /// <param name="config">网关配置</param>
    /// <returns>创建的网关配置</returns>
    public async Task<GatewayConfig> CreateConfigAsync(GatewayConfig config)
    {
        var entity = MapToEntity(config);
        entity.Id = Guid.NewGuid().ToString();
        entity.Version = await GenerateVersionAsync();
        entity.CreatedAt = DateTimeOffset.UtcNow;
        entity.CreatedBy = "Admin";

        await _dbContext.GatewayConfigs.AddAsync(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created new config: {ConfigId}, Version: {Version}", entity.Id, entity.Version);

        return MapToModel(entity);
    }

    /// <summary>
    /// 更新配置
    /// </summary>
    /// <param name="id">配置ID</param>
    /// <param name="config">网关配置</param>
    /// <returns>更新后的网关配置</returns>
    public async Task<GatewayConfig> UpdateConfigAsync(string id, GatewayConfig config)
    {
        var existing = await _dbContext.GatewayConfigs
            .Include(g => g.Routes)
            .Include(g => g.Clusters)
            .ThenInclude(c => c.Destinations)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (existing == null)
        {
            return new GatewayConfig();
        }

        // 删除旧的关联数据
        _dbContext.Routes.RemoveRange(existing.Routes);
        _dbContext.Destinations.RemoveRange(existing.Clusters.SelectMany(c => c.Destinations));
        _dbContext.Clusters.RemoveRange(existing.Clusters);

        // 更新配置
        existing.Version = await GenerateVersionAsync();
        existing.SecurityJson = JsonSerializer.Serialize(config.Security);
        existing.MonitoringJson = JsonSerializer.Serialize(config.Monitoring);

        // 添加新的路由
        foreach (var route in config.Routes.Values)
        {
            var routeEntity = new RouteConfigEntity
            {
                Id = route.Id,
                Name = route.Name,
                ClusterId = route.ClusterId,
                MatchJson = JsonSerializer.Serialize(route.Match),
                TransformsJson = JsonSerializer.Serialize(route.Transforms),
                Enabled = route.Enabled,
                GatewayConfigId = existing.Id
            };
            existing.Routes.Add(routeEntity);
        }

        // 添加新的集群
        foreach (var cluster in config.Clusters.Values)
        {
            var clusterEntity = new ClusterConfigEntity
            {
                Id = cluster.Id,
                Name = cluster.Name,
                LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                HealthCheckJson = JsonSerializer.Serialize(cluster.HealthCheck),
                GatewayConfigId = existing.Id
            };

            // 添加目标
            foreach (var destination in cluster.Destinations.Values)
            {
                var destinationEntity = new DestinationConfigEntity
                {
                    Id = destination.Address, // 使用地址作为ID
                    Address = destination.Address,
                    Healthy = destination.Healthy,
                    ClusterConfigId = cluster.Id
                };
                clusterEntity.Destinations.Add(destinationEntity);
            }

            existing.Clusters.Add(clusterEntity);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Updated config: {ConfigId}, Version: {Version}", existing.Id, existing.Version);

        return MapToModel(existing);
    }

    /// <summary>
    /// 删除配置
    /// </summary>
    /// <param name="id">配置ID</param>
    public async Task DeleteConfigAsync(string id)
    {
        var config = await _dbContext.GatewayConfigs.FirstOrDefaultAsync(g => g.Id == id);
        if (config != null)
        {
            _dbContext.GatewayConfigs.Remove(config);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Deleted config: {ConfigId}", id);
        }
    }

    /// <summary>
    /// 生成版本号
    /// </summary>
    /// <returns>版本号</returns>
    private async Task<string> GenerateVersionAsync()
    {
        var count = await _dbContext.GatewayConfigs.CountAsync();
        return $"1.0.{count + 1}";
    }

    /// <summary>
    /// 将实体映射到模型
    /// </summary>
    /// <param name="entity">配置实体</param>
    /// <returns>网关配置</returns>
    private GatewayConfig MapToModel(GatewayConfigEntity entity)
    {
        var config = new GatewayConfig
        {
            Id = entity.Id,
            Version = entity.Version,
            CreatedAt = entity.CreatedAt,
            CreatedBy = entity.CreatedBy,
            Security = JsonSerializer.Deserialize<SecurityConfig>(entity.SecurityJson) ?? new SecurityConfig(),
            Monitoring = JsonSerializer.Deserialize<MonitoringConfig>(entity.MonitoringJson) ?? new MonitoringConfig(),
            Routes = new Dictionary<string, RouteConfig>(),
            Clusters = new Dictionary<string, ClusterConfig>()
        };

        // 映射路由
        foreach (var routeEntity in entity.Routes)
        {
            var route = new RouteConfig
            {
                Id = routeEntity.Id,
                Name = routeEntity.Name,
                ClusterId = routeEntity.ClusterId,
                Match = JsonSerializer.Deserialize<RouteMatch>(routeEntity.MatchJson) ?? new RouteMatch(),
                Transforms = JsonSerializer.Deserialize<RouteTransforms>(routeEntity.TransformsJson) ?? new RouteTransforms(),
                Enabled = routeEntity.Enabled
            };
            config.Routes[route.Id] = route;
        }

        // 映射集群
        foreach (var clusterEntity in entity.Clusters)
        {
            var cluster = new ClusterConfig
            {
                Id = clusterEntity.Id,
                Name = clusterEntity.Name,
                LoadBalancingPolicy = clusterEntity.LoadBalancingPolicy,
                HealthCheck = JsonSerializer.Deserialize<HealthCheckConfig>(clusterEntity.HealthCheckJson) ?? new HealthCheckConfig(),
                Destinations = new Dictionary<string, DestinationConfig>()
            };

            // 映射目标
            foreach (var destinationEntity in clusterEntity.Destinations)
            {
                var destination = new DestinationConfig
                {
                    Address = destinationEntity.Address,
                    Healthy = destinationEntity.Healthy
                };
                cluster.Destinations[destination.Address] = destination;
            }

            config.Clusters[cluster.Id] = cluster;
        }

        return config;
    }

    /// <summary>
    /// 将模型映射到实体
    /// </summary>
    /// <param name="config">网关配置</param>
    /// <returns>配置实体</returns>
    private GatewayConfigEntity MapToEntity(GatewayConfig config)
    {
        var entity = new GatewayConfigEntity
        {
            Id = config.Id,
            Version = config.Version,
            CreatedAt = config.CreatedAt,
            CreatedBy = config.CreatedBy,
            SecurityJson = JsonSerializer.Serialize(config.Security),
            MonitoringJson = JsonSerializer.Serialize(config.Monitoring),
            Routes = new List<RouteConfigEntity>(),
            Clusters = new List<ClusterConfigEntity>()
        };

        // 映射路由
        foreach (var route in config.Routes.Values)
        {
            var routeEntity = new RouteConfigEntity
            {
                Id = route.Id,
                Name = route.Name,
                ClusterId = route.ClusterId,
                MatchJson = JsonSerializer.Serialize(route.Match),
                TransformsJson = JsonSerializer.Serialize(route.Transforms),
                Enabled = route.Enabled,
                GatewayConfigId = config.Id
            };
            entity.Routes.Add(routeEntity);
        }

        // 映射集群
        foreach (var cluster in config.Clusters.Values)
        {
            var clusterEntity = new ClusterConfigEntity
            {
                Id = cluster.Id,
                Name = cluster.Name,
                LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                HealthCheckJson = JsonSerializer.Serialize(cluster.HealthCheck),
                GatewayConfigId = config.Id,
                Destinations = new List<DestinationConfigEntity>()
            };

            // 映射目标
            foreach (var destination in cluster.Destinations.Values)
            {
                var destinationEntity = new DestinationConfigEntity
                {
                    Id = destination.Address, // 使用地址作为ID
                    Address = destination.Address,
                    Healthy = destination.Healthy,
                    ClusterConfigId = cluster.Id
                };
                clusterEntity.Destinations.Add(destinationEntity);
            }

            entity.Clusters.Add(clusterEntity);
        }

        return entity;
    }
}

/// <summary>
/// 数据库配置服务接口
/// </summary>
public interface IDbConfigService
{
    /// <summary>
    /// 获取最新配置
    /// </summary>
    /// <returns>网关配置</returns>
    Task<GatewayConfig> GetLatestConfigAsync();

    /// <summary>
    /// 根据版本获取配置
    /// </summary>
    /// <param name="version">版本号</param>
    /// <returns>网关配置</returns>
    Task<GatewayConfig> GetConfigByVersionAsync(string version);

    /// <summary>
    /// 创建配置
    /// </summary>
    /// <param name="config">网关配置</param>
    /// <returns>创建的网关配置</returns>
    Task<GatewayConfig> CreateConfigAsync(GatewayConfig config);

    /// <summary>
    /// 更新配置
    /// </summary>
    /// <param name="id">配置ID</param>
    /// <param name="config">网关配置</param>
    /// <returns>更新后的网关配置</returns>
    Task<GatewayConfig> UpdateConfigAsync(string id, GatewayConfig config);

    /// <summary>
    /// 删除配置
    /// </summary>
    /// <param name="id">配置ID</param>
    Task DeleteConfigAsync(string id);
}
