using Microsoft.EntityFrameworkCore;
using MinGo.Shared.Models;
using MinGo.Shared.Data.Configuration;

namespace MinGo.Shared.Data;

public class ProxyDbContext : DbContext
{
    public DbSet<ApiRouteEntity> Routes { get; set; }
    public DbSet<ApiClusterEntity> Clusters { get; set; }
    public DbSet<ApiDestinationEntity> Destinations { get; set; }

    public ProxyDbContext(DbContextOptions<ProxyDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ApiRouteEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ApiClusterEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ApiDestinationEntityConfiguration());
    }
}
