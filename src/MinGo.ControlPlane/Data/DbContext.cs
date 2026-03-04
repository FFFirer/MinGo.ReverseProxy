using Microsoft.EntityFrameworkCore;
using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Data;

/// <summary>
/// 数据库上下文类
/// </summary>
public class GatewayDbContext : DbContext
{
    /// <summary>
    /// 配置表
    /// </summary>
    public DbSet<GatewayConfigEntity> GatewayConfigs { get; set; }

    /// <summary>
    /// 路由表
    /// </summary>
    public DbSet<RouteConfigEntity> Routes { get; set; }

    /// <summary>
    /// 集群表
    /// </summary>
    public DbSet<ClusterConfigEntity> Clusters { get; set; }

    /// <summary>
    /// 目标表
    /// </summary>
    public DbSet<DestinationConfigEntity> Destinations { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="options">数据库选项</param>
    public GatewayDbContext(DbContextOptions<GatewayDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// 模型创建
    /// </summary>
    /// <param name="modelBuilder">模型构建器</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置关系
        modelBuilder.Entity<GatewayConfigEntity>()
            .HasMany(g => g.Routes)
            .WithOne(r => r.GatewayConfig)
            .HasForeignKey(r => r.GatewayConfigId);

        modelBuilder.Entity<GatewayConfigEntity>()
            .HasMany(g => g.Clusters)
            .WithOne(c => c.GatewayConfig)
            .HasForeignKey(c => c.GatewayConfigId);

        modelBuilder.Entity<ClusterConfigEntity>()
            .HasMany(c => c.Destinations)
            .WithOne(d => d.ClusterConfig)
            .HasForeignKey(d => d.ClusterConfigId);

        // 配置主键
        modelBuilder.Entity<GatewayConfigEntity>().HasKey(g => g.Id);
        modelBuilder.Entity<RouteConfigEntity>().HasKey(r => r.Id);
        modelBuilder.Entity<ClusterConfigEntity>().HasKey(c => c.Id);
        modelBuilder.Entity<DestinationConfigEntity>().HasKey(d => d.Id);
    }
}

/// <summary>
/// 网关配置实体
/// </summary>
public class GatewayConfigEntity
{
    /// <summary>
    /// 配置ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 版本号
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 创建人
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 安全配置（JSON存储）
    /// </summary>
    public string SecurityJson { get; set; } = string.Empty;

    /// <summary>
    /// 监控配置（JSON存储）
    /// </summary>
    public string MonitoringJson { get; set; } = string.Empty;

    /// <summary>
    /// 关联的路由
    /// </summary>
    public List<RouteConfigEntity> Routes { get; set; } = new();

    /// <summary>
    /// 关联的集群
    /// </summary>
    public List<ClusterConfigEntity> Clusters { get; set; } = new();
}

/// <summary>
/// 路由配置实体
/// </summary>
public class RouteConfigEntity
{
    /// <summary>
    /// 路由ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 路由名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 集群ID
    /// </summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>
    /// 路径匹配（JSON存储）
    /// </summary>
    public string MatchJson { get; set; } = string.Empty;

    /// <summary>
    /// 转换配置（JSON存储）
    /// </summary>
    public string TransformsJson { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 关联的网关配置ID
    /// </summary>
    public string GatewayConfigId { get; set; } = string.Empty;

    /// <summary>
    /// 关联的网关配置
    /// </summary>
    public GatewayConfigEntity GatewayConfig { get; set; } = null!;
}

/// <summary>
/// 集群配置实体
/// </summary>
public class ClusterConfigEntity
{
    /// <summary>
    /// 集群ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 集群名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public string LoadBalancingPolicy { get; set; } = "RoundRobin";

    /// <summary>
    /// 健康检查配置（JSON存储）
    /// </summary>
    public string HealthCheckJson { get; set; } = string.Empty;

    /// <summary>
    /// 关联的网关配置ID
    /// </summary>
    public string GatewayConfigId { get; set; } = string.Empty;

    /// <summary>
    /// 关联的网关配置
    /// </summary>
    public GatewayConfigEntity GatewayConfig { get; set; } = null!;

    /// <summary>
    /// 关联的目标
    /// </summary>
    public List<DestinationConfigEntity> Destinations { get; set; } = new();
}

/// <summary>
/// 目标配置实体
/// </summary>
public class DestinationConfigEntity
{
    /// <summary>
    /// 目标ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 目标地址
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 是否健康
    /// </summary>
    public bool Healthy { get; set; } = true;

    /// <summary>
    /// 关联的集群配置ID
    /// </summary>
    public string ClusterConfigId { get; set; } = string.Empty;

    /// <summary>
    /// 关联的集群配置
    /// </summary>
    public ClusterConfigEntity ClusterConfig { get; set; } = null!;
}
