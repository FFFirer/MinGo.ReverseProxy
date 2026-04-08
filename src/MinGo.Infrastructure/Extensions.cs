using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinGo.Core.Interfaces;
using MinGo.Infrastructure.ExternalServices;
using MinGo.Infrastructure.Services;
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

        // 注册证书管理器
        builder.Services.AddSingleton<ICertificateManager, CertificateManager>();

        return builder;
    }

    /// <summary>
    /// 在应用启动时初始化证书
    /// </summary>
    /// <param name="app">Web应用</param>
    /// <returns>任务</returns>
    public static async Task InitializeCertificatesAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var certificateManager = scope.ServiceProvider.GetService<ICertificateManager>();
        if (certificateManager != null)
        {
            await certificateManager.ReloadCertificatesAsync();
            app.Logger.LogInformation("Certificates initialized successfully");
        }
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

    /// <summary>
    /// 注册证书管理服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCertificateManager(this IServiceCollection services)
    {
        // 注册证书管理器
        services.AddSingleton<ICertificateManager, CertificateManager>();
        
        return services;
    }

    /// <summary>
    /// 配置 Kestrel 使用 SNI 进行证书选择
    /// </summary>
    /// <param name="builder">Kestrel 服务器选项配置</param>
    /// <param name="defaultCertificate">默认证书（开发证书）</param>
    /// <param name="isDevelopment">是否为开发环境</param>
    /// <param name="logger">日志记录器</param>
    public static void ConfigureKestrelSni(
        this KestrelServerOptions builder, 
        X509Certificate2? defaultCertificate,
        bool isDevelopment,
        ILogger? logger = null)
    {
        // 获取配置中的代理端口
        var proxyPorts = builder.ApplicationServices
            .GetRequiredService<IConfiguration>()
            .GetSection("ProxyPorts")
            .Get<int[]>() ?? Array.Empty<int>();

        foreach (var port in proxyPorts)
        {
            builder.ListenAnyIP(port, listenOptions =>
            {
                // 配置 HTTPS
                listenOptions.UseHttps(httpsOptions =>
                {
                    httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | 
                                                System.Security.Authentication.SslProtocols.Tls13;
                    
                    // 设置默认证书
                    if (defaultCertificate != null)
                    {
                        httpsOptions.ServerCertificate = defaultCertificate;
                    }
                    
                    // 在开发环境下，启用证书选择回调（如果可用）
                    if (isDevelopment)
                    {
                        // 开发环境使用默认证书，允许后续通过中间件动态更新
                        logger?.LogDebug("Kestrel configured for development environment on port {Port}", port);
                    }
                });
            });
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