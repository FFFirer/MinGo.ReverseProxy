using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinGo.Infrastructure.ExternalServices;
using Yarp.ReverseProxy.Configuration;

namespace MinGo.Infrastructure;
/// <summary>
/// YARP反向代理扩展方法
/// </summary>
public static class ReverseProxyExtensions
{
    /// <summary>
    /// 从数据库加载反向代理配置
    /// </summary>
    /// <param name="builder">反向代理构建器</param>
    /// <returns>反向代理构建器</returns>
    public static IReverseProxyBuilder LoadFromDatabase(this IReverseProxyBuilder builder)
    {
        // 注册数据库配置提供程序
        builder.Services.AddSingleton<DatabaseProxyConfigProvider>();
        builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DatabaseProxyConfigProvider>());

        return builder;
    }

    public static void UseDevelopmentAutoMigration(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            using var scope = app.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AutoMigration");
            var dbContext = scope.ServiceProvider.GetRequiredService<Data.ApiDbContext>();
            dbContext.Database.Migrate();

            logger.LogInformation("Auto apply migrations!");
        }
    }
}

/// <summary>
/// HttpClient 扩展方法
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// 从配置中注册命名的 HttpClient
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddNamedHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        var httpClientConfig = configuration.GetSection("HttpClient");
        var clientsConfig = httpClientConfig.GetSection("Clients");

        if (clientsConfig.Exists())
        {
            var clientSections = clientsConfig.GetChildren();
            foreach (var clientSection in clientSections)
            {
                var clientName = clientSection.Key;
                var baseAddress = clientSection.GetValue<string>("BaseAddress");
                var timeout = clientSection.GetValue<TimeSpan?>("Timeout") ?? TimeSpan.FromSeconds(30);

                if (!string.IsNullOrEmpty(baseAddress))
                {
                    services.AddHttpClient(clientName, client =>
                    {
                        client.BaseAddress = new Uri(baseAddress);
                        client.Timeout = timeout;
                    });
                }
            }
        }

        return services;
    }
}