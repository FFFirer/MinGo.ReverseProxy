using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.Infrastructure.Data;

/// <summary>
/// API数据库服务
/// </summary>
public class ApiDbService : IApiDbService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ApiDbService> _logger;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="dbContext">数据库上下文</param>
    /// <param name="logger">日志记录器</param>
    public ApiDbService(AppDbContext dbContext, ILogger<ApiDbService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有路由
    /// </summary>
    /// <returns>路由列表</returns>
    public async Task<IEnumerable<RouteConfig>> GetRoutesAsync()
    {
        var entities = await _dbContext.Routes.ToListAsync();
        return entities.Select(MapToRouteModel);
    }

    /// <summary>
    /// 根据ID获取路由
    /// </summary>
    /// <param name="id">路由ID</param>
    /// <returns>路由配置</returns>
    public async Task<RouteConfig?> GetRouteAsync(string id)
    {
        var entity = await _dbContext.Routes.FirstOrDefaultAsync(r => r.Id == id);
        return entity != null ? MapToRouteModel(entity) : null;
    }

    /// <summary>
    /// 创建路由
    /// </summary>
    /// <param name="route">路由配置</param>
    /// <returns>创建的路由</returns>
    public async Task<RouteConfig> CreateRouteAsync(RouteConfig route)
    {
        var entity = MapToRouteEntity(route);
        entity.Id = Guid.NewGuid().ToString();
        entity.CreatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.Routes.AddAsync(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created route: {RouteId}", entity.Id);
        
        return MapToRouteModel(entity);
    }

    /// <summary>
    /// 更新路由
    /// </summary>
    /// <param name="id">路由ID</param>
    /// <param name="route">路由配置</param>
    /// <returns>更新后的路由</returns>
    public async Task<RouteConfig?> UpdateRouteAsync(string id, RouteConfig route)
    {
        var existing = await _dbContext.Routes.FirstOrDefaultAsync(r => r.Id == id);
        if (existing == null)
        {
            return null;
        }

        existing.Name = route.Name;
        existing.ClusterId = route.ClusterId;
        existing.MatchJson = JsonSerializer.Serialize(route.Match);
        existing.TransformsJson = JsonSerializer.Serialize(route.Transforms);
        existing.Enabled = route.Enabled;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Updated route: {RouteId}", id);

        return MapToRouteModel(existing);
    }

    /// <summary>
    /// 删除路由
    /// </summary>
    /// <param name="id">路由ID</param>
    public async Task DeleteRouteAsync(string id)
    {
        var route = await _dbContext.Routes.FirstOrDefaultAsync(r => r.Id == id);
        if (route != null)
        {
            _dbContext.Routes.Remove(route);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Deleted route: {RouteId}", id);
        }
    }

    /// <summary>
    /// 获取所有集群
    /// </summary>
    /// <returns>集群列表</returns>
    public async Task<IEnumerable<ClusterConfig>> GetClustersAsync()
    {
        var entities = await _dbContext.Clusters
            .Include(c => c.Destinations)
            .ToListAsync();
        return entities.Select(MapToClusterModel);
    }

    /// <summary>
    /// 根据ID获取集群
    /// </summary>
    /// <param name="id">集群ID</param>
    /// <returns>集群配置</returns>
    public async Task<ClusterConfig?> GetClusterAsync(string id)
    {
        var entity = await _dbContext.Clusters
            .Include(c => c.Destinations)
            .FirstOrDefaultAsync(c => c.Id == id);
        return entity != null ? MapToClusterModel(entity) : null;
    }

    /// <summary>
    /// 创建集群
    /// </summary>
    public async Task<ClusterConfig> CreateClusterAsync(ClusterConfig cluster)
    {
        var entity = MapToClusterEntity(cluster);
        entity.Id = cluster.Id;
        entity.CreatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        // 添加目标 — 自动生成 ID
        var seq = 1;
        foreach (var destination in cluster.Destinations)
        {
            var generatedId = $"{entity.Id}-{seq++}";
            var destinationEntity = new ApiDestinationEntity
            {
                Id = generatedId,
                Address = destination.Address,
                Healthy = true,
                ClusterId = entity.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            entity.Destinations.Add(destinationEntity);
        }

        await _dbContext.Clusters.AddAsync(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created cluster: {ClusterId}", entity.Id);
        
        return MapToClusterModel(entity);
    }

    /// <summary>
    /// 更新集群
    /// </summary>
    /// <param name="id">集群ID</param>
    /// <param name="cluster">集群配置</param>
    /// <returns>更新后的集群</returns>
    public async Task<ClusterConfig?> UpdateClusterAsync(string id, ClusterConfig cluster)
    {
        var existing = await _dbContext.Clusters
            .Include(c => c.Destinations)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (existing == null)
        {
            return null;
        }

        // 更新集群信息
        existing.LoadBalancingPolicy = cluster.LoadBalancingPolicy;
        existing.HealthCheckJson = JsonSerializer.Serialize(cluster.HealthCheck);
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        // 删除旧的目标
        _dbContext.Destinations.RemoveRange(existing.Destinations);
        existing.Destinations.Clear();

        // 添加新的目标 — 保留已有 ID，新目标自动生成
        var seq = await GetNextDestinationSequenceAsync(id);
        foreach (var destination in cluster.Destinations)
        {
            var destId = string.IsNullOrEmpty(destination.Id)
                ? $"{id}-{seq++}"
                : destination.Id;

            var destinationEntity = new ApiDestinationEntity
            {
                Id = destId,
                Address = destination.Address,
                Healthy = true,
                ClusterId = existing.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            existing.Destinations.Add(destinationEntity);
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Updated cluster: {ClusterId}", id);

        return MapToClusterModel(existing);
    }

    /// <summary>
    /// 删除集群
    /// </summary>
    /// <param name="id">集群ID</param>
    public async Task DeleteClusterAsync(string id)
    {
        var cluster = await _dbContext.Clusters.FirstOrDefaultAsync(c => c.Id == id);
        if (cluster != null)
        {
            _dbContext.Clusters.Remove(cluster);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Deleted cluster: {ClusterId}", id);
        }
    }

    /// <summary>
    /// 添加目标（后端自动生成 ID）
    /// </summary>
    /// <param name="clusterId">集群ID</param>
    /// <param name="destination">目标配置（不需传id）</param>
    /// <returns>更新后的集群</returns>
    public async Task<ClusterConfig?> AddDestinationAsync(string clusterId, DestinationConfig destination)
    {
        var cluster = await _dbContext.Clusters
            .Include(c => c.Destinations)
            .FirstOrDefaultAsync(c => c.Id == clusterId);
        if (cluster == null)
        {
            return null;
        }

        var seq = await GetNextDestinationSequenceAsync(clusterId);
        var generatedId = $"{clusterId}-{seq}";

        var destinationEntity = new ApiDestinationEntity
        {
            Id = generatedId,
            Address = destination.Address,
            Healthy = true,
            ClusterId = clusterId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        cluster.Destinations.Add(destinationEntity);
        cluster.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Added destination {DestinationId} to cluster {ClusterId}", generatedId, clusterId);

        return MapToClusterModel(cluster);
    }

    /// <summary>
    /// 移除目标
    /// </summary>
    /// <param name="clusterId">集群ID</param>
    /// <param name="destinationId">目标ID</param>
    /// <returns>更新后的集群</returns>
    public async Task<ClusterConfig?> RemoveDestinationAsync(string clusterId, string destinationId)
    {
        var cluster = await _dbContext.Clusters
            .Include(c => c.Destinations)
            .FirstOrDefaultAsync(c => c.Id == clusterId);
        if (cluster == null)
        {
            return null;
        }

        var destinationEntity = cluster.Destinations.FirstOrDefault(d => d.Id == destinationId);
        if (destinationEntity == null)
        {
            return null;
        }

        cluster.Destinations.Remove(destinationEntity);
        cluster.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Removed destination {DestinationId} from cluster {ClusterId}", destinationId, clusterId);

        return MapToClusterModel(cluster);
    }

    /// <summary>
    /// 获取集群内下一个目标序号（单调递增，永不重复）
    /// </summary>
    private async Task<int> GetNextDestinationSequenceAsync(string clusterId)
    {
        var prefix = clusterId + "-";
        var maxId = await _dbContext.Destinations
            .Where(d => d.ClusterId == clusterId)
            .OrderByDescending(d => d.Id)
            .Select(d => d.Id)
            .FirstOrDefaultAsync() ?? "";

        var maxSeq = 0;
        if (maxId.StartsWith(prefix))
        {
            int.TryParse(maxId[prefix.Length..], out maxSeq);
        }
        return maxSeq + 1;
    }

    /// <summary>
    /// 获取所有证书
    /// </summary>
    /// <returns>证书列表</returns>
    public async Task<IEnumerable<CertificateConfig>> GetCertificatesAsync()
    {
        var entities = await _dbContext.Certificates.ToListAsync();
        return entities.Select(MapToCertificateModel);
    }

    /// <summary>
    /// 根据ID获取证书
    /// </summary>
    /// <param name="id">证书ID</param>
    /// <returns>证书配置</returns>
    public async Task<CertificateConfig?> GetCertificateAsync(string id)
    {
        var entity = await _dbContext.Certificates.FirstOrDefaultAsync(c => c.Id == id);
        return entity != null ? MapToCertificateModel(entity) : null;
    }

    /// <summary>
    /// 创建证书
    /// </summary>
    /// <param name="certificate">证书配置</param>
    /// <returns>创建的证书</returns>
    public async Task<CertificateConfig> CreateCertificateAsync(CertificateConfig certificate)
    {
        var entity = MapToCertificateEntity(certificate);
        entity.Id = Guid.NewGuid().ToString();
        entity.CreatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.Certificates.AddAsync(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created certificate: {CertificateId} for domain {DomainName}", entity.Id, entity.DomainName);
        return MapToCertificateModel(entity);
    }

    /// <summary>
    /// 更新证书
    /// </summary>
    /// <param name="id">证书ID</param>
    /// <param name="certificate">证书配置</param>
    /// <returns>更新后的证书</returns>
    public async Task<CertificateConfig?> UpdateCertificateAsync(string id, CertificateConfig certificate)
    {
        var existing = await _dbContext.Certificates.FirstOrDefaultAsync(c => c.Id == id);
        if (existing == null)
        {
            return null;
        }

        existing.DomainName = certificate.DomainName;
        existing.CertificateType = certificate.CertificateType;
        existing.ExpiresAt = certificate.ExpiresAt;
        existing.Subject = certificate.Subject;
        existing.Issuer = certificate.Issuer;
        existing.Thumbprint = certificate.Thumbprint;
        existing.IsValid = certificate.IsValid;
        existing.CertificateData = certificate.CertificateData;
        existing.Password = certificate.Password;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Updated certificate: {CertificateId}", id);

        return MapToCertificateModel(existing);
    }

    /// <summary>
    /// 删除证书
    /// </summary>
    /// <param name="id">证书ID</param>
    public async Task DeleteCertificateAsync(string id)
    {
        var certificate = await _dbContext.Certificates.FirstOrDefaultAsync(c => c.Id == id);
        if (certificate != null)
        {
            _dbContext.Certificates.Remove(certificate);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Deleted certificate: {CertificateId}", id);
        }
    }

    /// <summary>
    /// 初始化示例数据
    /// </summary>
    public async Task InitializeSampleDataAsync()
    {
        // 检查是否已有数据
        if (await _dbContext.Routes.AnyAsync() || await _dbContext.Clusters.AnyAsync())
        {
            return;
        }

        // 创建示例集群（名称即 ID）
        var cluster1 = new ClusterConfig
        {
            Id = "user-cluster",
            LoadBalancingPolicy = "RoundRobin",
            Destinations = new List<DestinationConfig>
            {
                new DestinationConfig { Address = "http://localhost:5001", Healthy = true },
                new DestinationConfig { Address = "http://localhost:5002", Healthy = true }
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

        var cluster2 = new ClusterConfig
        {
            Id = "product-cluster",
            LoadBalancingPolicy = "LeastRequests",
            Destinations = new List<DestinationConfig>
            {
                new DestinationConfig { Address = "http://localhost:6001", Healthy = true },
                new DestinationConfig { Address = "http://localhost:6002", Healthy = false }
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

        // 创建集群
        await CreateClusterAsync(cluster1);
        await CreateClusterAsync(cluster2);

        // 创建示例路由
        var route1 = new RouteConfig
        {
            Id = "route1",
            Name = "用户服务路由",
            ClusterId = "user-cluster",
            Match = new RouteMatch { Path = "/api/users/{**catch-all}" },
            Enabled = true
        };

        var route2 = new RouteConfig
        {
            Id = "route2",
            Name = "产品服务路由",
            ClusterId = "product-cluster",
            Match = new RouteMatch { Path = "/api/products/{**catch-all}" },
            Enabled = true
        };

        var route3 = new RouteConfig
        {
            Id = "route3",
            Name = "API网关用户服务",
            ClusterId = "user-cluster",
            Match = new RouteMatch { Host = "api.example.com", Path = "/api/users/{**catch-all}" },
            Enabled = true
        };

        var route4 = new RouteConfig
        {
            Id = "route4",
            Name = "管理后台API",
            ClusterId = "product-cluster",
            Match = new RouteMatch { Host = "admin.example.com", Path = "/api/{**catch-all}" },
            Enabled = false
        };

        // 创建路由
        await CreateRouteAsync(route1);
        await CreateRouteAsync(route2);
        await CreateRouteAsync(route3);
        await CreateRouteAsync(route4);

        // 创建示例证书
        var cert1 = new CertificateConfig
        {
            Id = "cert1",
            DomainName = "api.example.com",
            CertificateType = "Pfx",
            CreatedAt = DateTimeOffset.Now.AddMonths(-3),
            ExpiresAt = DateTimeOffset.Now.AddMonths(9),
            Subject = "CN=api.example.com",
            Issuer = "Let's Encrypt Authority X3",
            Thumbprint = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6",
            IsValid = true
        };

        var cert2 = new CertificateConfig
        {
            Id = "cert2",
            DomainName = "admin.example.com",
            CertificateType = "Pfx",
            CreatedAt = DateTimeOffset.Now.AddMonths(-6),
            ExpiresAt = DateTimeOffset.Now.AddMonths(6),
            Subject = "CN=admin.example.com",
            Issuer = "Let's Encrypt Authority X3",
            Thumbprint = "f6e5d4c3b2a1f0e9d8c7b6a5f4e3d2c1",
            IsValid = true
        };

        // 创建证书
        await CreateCertificateAsync(cert1);
        await CreateCertificateAsync(cert2);

        _logger.LogInformation("Initialized sample data");
    }

    /// <summary>
    /// 将路由实体映射到模型
    /// </summary>
    /// <param name="entity">路由实体</param>
    /// <returns>路由配置</returns>
    private RouteConfig MapToRouteModel(ApiRouteEntity entity)
    {
        return new RouteConfig
        {
            Id = entity.Id,
            Name = entity.Name,
            ClusterId = entity.ClusterId,
            Match = JsonSerializer.Deserialize<RouteMatch>(entity.MatchJson) ?? new RouteMatch(),
            Transforms = JsonSerializer.Deserialize<RouteTransforms>(entity.TransformsJson) ?? new RouteTransforms(),
            Enabled = entity.Enabled
        };
    }

    /// <summary>
    /// 将路由模型映射到实体
    /// </summary>
    /// <param name="model">路由配置</param>
    /// <returns>路由实体</returns>
    private ApiRouteEntity MapToRouteEntity(RouteConfig model)
    {
        return new ApiRouteEntity
        {
            Id = model.Id,
            Name = model.Name,
            ClusterId = model.ClusterId,
            MatchJson = JsonSerializer.Serialize(model.Match),
            TransformsJson = JsonSerializer.Serialize(model.Transforms),
            Enabled = model.Enabled
        };
    }

    /// <summary>
    /// 将集群实体映射到模型
    /// </summary>
    /// <param name="entity">集群实体</param>
    /// <returns>集群配置</returns>
    private ClusterConfig MapToClusterModel(ApiClusterEntity entity)
    {
        var model = new ClusterConfig
        {
            Id = entity.Id,
            LoadBalancingPolicy = entity.LoadBalancingPolicy,
            HealthCheck = JsonSerializer.Deserialize<HealthCheckConfig>(entity.HealthCheckJson) ?? new HealthCheckConfig(),
            Destinations = entity.Destinations.Select(d => new DestinationConfig
            {
                Id = d.Id,
                Address = d.Address,
                Healthy = d.Healthy,
            }).ToList()
        };

        return model;
    }

    /// <summary>
    /// 将集群模型映射到实体
    /// </summary>
    /// <param name="model">集群配置</param>
    /// <returns>集群实体</returns>
    private ApiClusterEntity MapToClusterEntity(ClusterConfig model)
    {
        return new ApiClusterEntity
        {
            Id = model.Id,
            LoadBalancingPolicy = model.LoadBalancingPolicy,
            HealthCheckJson = JsonSerializer.Serialize(model.HealthCheck),
            Destinations = new List<ApiDestinationEntity>()
        };
    }

    /// <summary>
    /// 将证书实体映射到模型
    /// </summary>
    /// <param name="entity">证书实体</param>
    /// <returns>证书配置</returns>
    private CertificateConfig MapToCertificateModel(ApiCertificateEntity entity)
    {
        return new CertificateConfig
        {
            Id = entity.Id,
            DomainName = entity.DomainName,
            CertificateType = entity.CertificateType,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt,
            Subject = entity.Subject,
            Issuer = entity.Issuer,
            Thumbprint = entity.Thumbprint,
            IsValid = entity.IsValid,
            CertificateData = entity.CertificateData,
            Password = entity.Password
        };
    }

    /// <summary>
    /// 将证书模型映射到实体
    /// </summary>
    /// <param name="model">证书配置</param>
    /// <returns>证书实体</returns>
    private ApiCertificateEntity MapToCertificateEntity(CertificateConfig model)
    {
        return new ApiCertificateEntity
        {
            Id = model.Id,
            DomainName = model.DomainName,
            CertificateType = model.CertificateType,
            CreatedAt = model.CreatedAt,
            ExpiresAt = model.ExpiresAt,
            Subject = model.Subject,
            Issuer = model.Issuer,
            Thumbprint = model.Thumbprint,
            IsValid = model.IsValid,
            CertificateData = model.CertificateData,
            Password = model.Password
        };
    }
}