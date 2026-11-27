using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MinGo.Core.Entities;
using MinGo.Infrastructure.Data.Configuration;

namespace MinGo.Infrastructure.Data;

/// <summary>
/// 统一数据库上下文 - 合并 API 管理实体与 ASP.NET Core Identity
/// </summary>
public class AppDbContext : IdentityDbContext<IdentityUser>
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
    /// Portainer 实例表
    /// </summary>
    public DbSet<PortainerInstanceEntity> PortainerInstances { get; set; }

    /// <summary>
    /// Cloudflare 接入配置表（单行）
    /// </summary>
    public DbSet<CloudflareSettingsEntity> CloudflareSettings { get; set; }

    /// <summary>
    /// 数据面节点表
    /// </summary>
    public DbSet<DataPlaneNodeEntity> DataPlaneNodes { get; set; }

    /// <summary>
    /// 实例节点绑定表
    /// </summary>
    public DbSet<InstanceNodeAssignmentEntity> InstanceNodeAssignments { get; set; }

    /// <summary>
    /// CertManager 远程接入配置表（单行）
    /// </summary>
    public DbSet<CertManagerSettingsEntity> CertManagerSettings { get; set; }

    /// <summary>
    /// 证书自动接入登记表
    /// </summary>
    public DbSet<CertificateEnrollmentEntity> CertificateEnrollments { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── Identity 自定义表名（不生成 AspNet* 默认表名） ──
        builder.Entity<IdentityUser>(entity => entity.ToTable("Users"));
        builder.Entity<IdentityRole>(entity => entity.ToTable("Roles"));
        builder.Entity<IdentityRoleClaim<string>>(entity => entity.ToTable("RoleClaims"));
        builder.Entity<IdentityUserClaim<string>>(entity => entity.ToTable("UserClaims"));
        builder.Entity<IdentityUserLogin<string>>(entity => entity.ToTable("UserLogins"));
        builder.Entity<IdentityUserRole<string>>(entity => entity.ToTable("UserRoles"));
        builder.Entity<IdentityUserToken<string>>(entity => entity.ToTable("UserTokens"));

        // ── API 管理实体配置 ──
        builder.ApplyConfiguration(new ApiRouteEntityConfiguration());
        builder.ApplyConfiguration(new ApiClusterEntityConfiguration());
        builder.ApplyConfiguration(new ApiDestinationEntityConfiguration());
        builder.ApplyConfiguration(new ApiCertificateEntityConfiguration());
        builder.ApplyConfiguration(new PortainerInstanceEntityConfiguration());
        builder.ApplyConfiguration(new CloudflareSettingsEntityConfiguration());
        builder.ApplyConfiguration(new DataPlaneNodeEntityConfiguration());
        builder.ApplyConfiguration(new InstanceNodeAssignmentEntityConfiguration());
        builder.ApplyConfiguration(new CertManagerSettingsEntityConfiguration());
        builder.ApplyConfiguration(new CertificateEnrollmentEntityConfiguration());
    }
}
