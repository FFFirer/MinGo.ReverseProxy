using Microsoft.EntityFrameworkCore;
using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Data;

/// <summary>
/// API管理数据库上下文
/// </summary>
public class ApiDbContext : DbContext
{
    /// <summary>
    /// 路由表
    /// </summary>
    public DbSet<ApiRouteEntity> Routes { get; set; }

    /// <summary>
    /// 集群表
    /// </summary>
    public DbSet<ApiClusterEntity> Clusters { get; set; }

    /// <summary>
    /// 目标表
    /// </summary>
    public DbSet<ApiDestinationEntity> Destinations { get; set; }

    /// <summary>
    /// 证书表
    /// </summary>
    public DbSet<ApiCertificateEntity> Certificates { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="options">数据库选项</param>
    public ApiDbContext(DbContextOptions<ApiDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// 模型创建
    /// </summary>
    /// <param name="modelBuilder">模型构建器</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置表名
        modelBuilder.Entity<ApiRouteEntity>().ToTable("ApiRoutes");
        modelBuilder.Entity<ApiClusterEntity>().ToTable("ApiClusters");
        modelBuilder.Entity<ApiDestinationEntity>().ToTable("ApiDestinations");
        modelBuilder.Entity<ApiCertificateEntity>().ToTable("ApiCertificates");

        // 配置关系
        modelBuilder.Entity<ApiClusterEntity>()
            .HasMany(c => c.Destinations)
            .WithOne(d => d.Cluster)
            .HasForeignKey(d => d.ClusterId);

        // 配置主键
        modelBuilder.Entity<ApiRouteEntity>().HasKey(r => r.Id);
        modelBuilder.Entity<ApiClusterEntity>().HasKey(c => c.Id);
        modelBuilder.Entity<ApiDestinationEntity>().HasKey(d => d.Id);
        modelBuilder.Entity<ApiCertificateEntity>().HasKey(c => c.Id);
    }
}

/// <summary>
/// API路由实体
/// </summary>
public class ApiRouteEntity
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
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// API集群实体
/// </summary>
public class ApiClusterEntity
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
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 关联的目标
    /// </summary>
    public List<ApiDestinationEntity> Destinations { get; set; } = new();
}

/// <summary>
/// API目标实体
/// </summary>
public class ApiDestinationEntity
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
    /// 关联的集群ID
    /// </summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>
    /// 关联的集群
    /// </summary>
    public ApiClusterEntity Cluster { get; set; } = null!;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// API证书实体
/// </summary>
public class ApiCertificateEntity
{
    /// <summary>
    /// 证书ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 域名
    /// </summary>
    public string DomainName { get; set; } = string.Empty;

    /// <summary>
    /// 证书类型
    /// </summary>
    public string CertificateType { get; set; } = "Pfx";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// 主题
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// 颁发者
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// 指纹
    /// </summary>
    public string Thumbprint { get; set; } = string.Empty;

    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; } = true;

    /// <summary>
    /// 证书数据
    /// </summary>
    public byte[]? CertificateData { get; set; }

    /// <summary>
    /// 密码
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
