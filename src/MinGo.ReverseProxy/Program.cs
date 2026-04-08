using Microsoft.EntityFrameworkCore;
using MinGo.Infrastructure.Data;
using MinGo.Application.Services;
using Serilog;
using Vite.AspNetCore;
using MinGo.Infrastructure;
using MinGo.Core.Services;
using MinGo.Core.Interfaces;
using MinGo.Infrastructure.ExternalServices;
using System.Security.Cryptography.X509Certificates;

Console.WriteLine("Starting MinGo Reverse Proxy...");

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

int[] AdminPorts = builder.Configuration.GetSection(nameof(AdminPorts)).Get<int[]>() ?? [];
int[] ProxyPorts = builder.Configuration.GetSection(nameof(ProxyPorts)).Get<int[]>() ?? [];

builder.Host.UseSerilog();
builder.Services.AddViteServices();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddControllers();
builder.Services.AddNamedHttpClients(builder.Configuration);

// 配置数据库
builder.Services.AddDbContext<ApiDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// 注册遥测存储
builder.Services.AddSingleton<TelemetryStore>();

// 注册服务
builder.Services.AddScoped<IApiDbService, MinGo.Infrastructure.Data.ApiDbService>();
builder.Services.AddScoped<IMonitoringService, MinGo.Application.Services.MonitoringService>();
builder.Services.AddScoped<ILogService, MinGo.Application.Services.LogService>();
builder.Services.AddScoped<IApiManagementService, MinGo.Application.Services.ApiManagementService>();
builder.Services.AddScoped<IGatewayInstanceService, MinGo.Application.Services.GatewayInstanceService>();
builder.Services.AddScoped<IGatewayEventSender, MinGo.Application.Services.GatewayEventSender>();
builder.Services.AddScoped<IGatewayEventService, MinGo.Application.Services.GatewayEventService>();
builder.Services.AddSingleton<IMessageNotificationService, MinGo.Application.Services.MemoryMessageNotificationService>();

// 注册配置更新事件监听器
builder.Services.AddHostedService<MinGo.Infrastructure.ExternalServices.ConfigUpdateEventListener>();

// 注册证书管理器
builder.Services.AddCertificateManager();

// 反向代理
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration)
    .LoadFromDatabase();

// 配置 Kestrel 使用 HTTPS 和 SNI 证书选择
builder.WebHost.ConfigureKestrel((context, options) =>
{
    // 原逻辑（Fallback）：使用 ConfigureHttpsDefaults 配置默认 SNI 选择器
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | 
                                   System.Security.Authentication.SslProtocols.Tls13;
        
        // Fallback 证书选择器：基于域名查找数据库证书，找不到时静默降级
        httpsOptions.ServerCertificateSelector = (connectionContext, serverName) =>
        {
            var certificateManager = CertificateSelector.CertificateManager;
            if (certificateManager != null)
            {
                return certificateManager.GetCertificate(serverName ?? "")!;
            }
            return null!;
        };
    });

    // 为代理端口配置监听
    foreach (var port in ProxyPorts)
    {
        options.ListenAnyIP(port, listenOptions =>
        {
            // 新逻辑：使用 HttpsConnectionAdapterOptions 的 ServerCertificateSelector
            // 这会覆盖 ConfigureHttpsDefaults 中的默认配置
            listenOptions.UseHttps(httpsOptions =>
            {
                httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | 
                                          System.Security.Authentication.SslProtocols.Tls13;
                
                // 新的证书选择器：优先使用域名精确匹配
                // 找不到时 fallback 到原逻辑
                httpsOptions.ServerCertificateSelector = (connectionContext, serverName) =>
                {
                    var domain = serverName ?? "";
                    
                    try
                    {
                        var certificateManager = CertificateSelector.CertificateManager;
                        if (certificateManager != null)
                        {
                            var cert = certificateManager.GetCertificate(domain);
                            if (cert != null)
                            {
                                return cert;
                            }
                        }
                        
                        // 新逻辑失败，fallback
                        var fallback = CertificateSelector.FallbackSelector;
                        return fallback != null 
                            ? fallback(connectionContext!, domain)! 
                            : null!;
                    }
                    catch
                    {
                        // 任何异常都 fallback
                        var fallback = CertificateSelector.FallbackSelector;
                        return fallback != null 
                            ? fallback(connectionContext!, domain)! 
                            : null!;
                    }
                };
            });
        });
    }
    
    // 管理端口保持 HTTP
    foreach (var port in AdminPorts)
    {
        options.ListenAnyIP(port);
    }
});

var app = builder.Build();

// 初始化证书并设置静态引用
await app.InitializeCertificatesAsync();
var certManager = app.Services.GetService<ICertificateManager>();
CertificateSelector.CertificateManager = certManager;

// 设置 Fallback 选择器（指向 ConfigureHttpsDefaults 中的默认逻辑）
CertificateSelector.FallbackSelector = (connectionContext, serverName) =>
{
    var manager = CertificateSelector.CertificateManager;
    return manager != null ? manager.GetCertificate(serverName) : null;
};

app.UseDevelopmentAutoMigration();

if(!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseGatewayTelemetry();

app.MapWhen(x => AdminPorts.Contains(x.Connection.LocalPort), b =>
{
    if (app.Environment.IsDevelopment())
    {
        b.UseViteDevelopmentServer(true);
    }

    b.UseStaticFiles();

    b.UseRouting();

    b.UseEndpoints(e =>
    {
        e.MapControllers();
        e.MapBlazorHub();
        e.MapFallbackToPage("/_Host");
    });
});

app.MapWhen(x => ProxyPorts.Contains(x.Connection.LocalPort), p =>
{
    p.UseRouting();
    p.UseEndpoints(e =>
    {
        e.MapReverseProxy();
    });
});

app.Run();

/// <summary>
/// 证书选择器静态持有器
/// 用于在 Kestrel TLS 回调中访问 CertificateManager 和 Fallback 逻辑
/// </summary>
internal static class CertificateSelector
{
    /// <summary>
    /// 证书管理器实例
    /// </summary>
    public static ICertificateManager? CertificateManager { get; set; }
    
    /// <summary>
    /// Fallback 证书选择器（由 ConfigureHttpsDefaults 设置的默认逻辑）
    /// </summary>
    public static Func<Microsoft.AspNetCore.Connections.ConnectionContext, string, X509Certificate2?>? FallbackSelector { get; set; }
}
