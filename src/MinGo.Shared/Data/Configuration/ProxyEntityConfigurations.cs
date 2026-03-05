using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MinGo.Shared.Models;

namespace MinGo.Shared.Data.Configuration;

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
