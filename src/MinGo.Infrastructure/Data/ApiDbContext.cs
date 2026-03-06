using Microsoft.EntityFrameworkCore;
using MinGo.Core.Entities;
using MinGo.Infrastructure.Data.Configuration;

namespace MinGo.Infrastructure.Data;

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

        modelBuilder.ApplyConfiguration(new ApiRouteEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ApiClusterEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ApiDestinationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ApiCertificateEntityConfiguration());
    }
}