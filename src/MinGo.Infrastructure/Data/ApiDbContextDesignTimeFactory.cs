using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using MinGo.Infrastructure.Data;
using System.IO;

namespace MinGo.Gitea.Infrastructure.Data;

public class ApiDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApiDbContext>
{
    public ApiDbContext CreateDbContext(string[] args)
    {
        // 构建配置
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(typeof(ApiDbContextDesignTimeFactory).Assembly)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        Console.WriteLine("Current using connectin string: {0}", connectionString);
        // 配置 DbContextOptions
        var optionsBuilder = new DbContextOptionsBuilder<ApiDbContext>();
        optionsBuilder.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(ApiDbContextDesignTimeFactory).Assembly));

        return new ApiDbContext(optionsBuilder.Options);
    }
}