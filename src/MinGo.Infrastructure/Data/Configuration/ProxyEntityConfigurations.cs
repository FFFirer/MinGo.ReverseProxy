using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MinGo.Core.Entities;

namespace MinGo.Infrastructure.Data.Configuration;

/// <summary>
/// API路由实体配置
/// </summary>
public class ApiRouteEntityConfiguration : IEntityTypeConfiguration<ApiRouteEntity>
{
    public void Configure(EntityTypeBuilder<ApiRouteEntity> builder)
    {
        builder.ToTable("ApiRoutes");
        builder.HasKey(r => r.Id);
    }
}

/// <summary>
/// API集群实体配置
/// </summary>
public class ApiClusterEntityConfiguration : IEntityTypeConfiguration<ApiClusterEntity>
{
    public void Configure(EntityTypeBuilder<ApiClusterEntity> builder)
    {
        builder.ToTable("ApiClusters");
        builder.HasKey(c => c.Id);
        
        builder.HasMany(c => c.Destinations)
            .WithOne(d => d.Cluster)
            .HasForeignKey(d => d.ClusterId);
    }
}

/// <summary>
/// API目标实体配置
/// </summary>
public class ApiDestinationEntityConfiguration : IEntityTypeConfiguration<ApiDestinationEntity>
{
    public void Configure(EntityTypeBuilder<ApiDestinationEntity> builder)
    {
        builder.ToTable("ApiDestinations");
        builder.HasKey(d => d.Id);
    }
}

/// <summary>
/// API证书实体配置
/// </summary>
public class ApiCertificateEntityConfiguration : IEntityTypeConfiguration<ApiCertificateEntity>
{
    public void Configure(EntityTypeBuilder<ApiCertificateEntity> builder)
    {
        builder.ToTable("ApiCertificates");
        builder.HasKey(c => c.Id);
    }
}

/// <summary>
/// 数据面节点实体配置
/// </summary>
public class DataPlaneNodeEntityConfiguration : IEntityTypeConfiguration<DataPlaneNodeEntity>
{
    public void Configure(EntityTypeBuilder<DataPlaneNodeEntity> builder)
    {
        builder.ToTable("DataPlaneNodes");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(64);
        builder.Property(e => e.Name).HasMaxLength(128);
    }
}

/// <summary>
/// 实例节点绑定实体配置（实例 Id 为主键）
/// </summary>
public class InstanceNodeAssignmentEntityConfiguration : IEntityTypeConfiguration<InstanceNodeAssignmentEntity>
{
    public void Configure(EntityTypeBuilder<InstanceNodeAssignmentEntity> builder)
    {
        builder.ToTable("InstanceNodeAssignments");
        builder.HasKey(e => e.InstanceId);
        builder.Property(e => e.InstanceId).HasMaxLength(128);
        builder.HasOne(e => e.Node)
            .WithMany()
            .HasForeignKey(e => e.NodeId);
    }
}

/// <summary>
/// Portainer 实例实体配置
/// </summary>
public class PortainerInstanceEntityConfiguration : IEntityTypeConfiguration<PortainerInstanceEntity>
{
    public void Configure(EntityTypeBuilder<PortainerInstanceEntity> builder)
    {
        builder.ToTable("PortainerInstances");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(128);
        builder.Property(e => e.Name).HasMaxLength(256);
        builder.Property(e => e.BaseUrl).HasMaxLength(1024);
        builder.Property(e => e.ApiKey).HasMaxLength(1024);
        builder.Property(e => e.HostAddress).HasMaxLength(1024);
    }
}

public class CloudflareSettingsEntityConfiguration : IEntityTypeConfiguration<CloudflareSettingsEntity>
{
    public void Configure(EntityTypeBuilder<CloudflareSettingsEntity> builder)
    {
        builder.ToTable("CloudflareSettings");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(64);
        builder.Property(e => e.ApiToken).HasMaxLength(1024);
        builder.Property(e => e.AccountId).HasMaxLength(128);
    }
}