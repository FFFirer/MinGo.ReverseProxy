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
using MinGo.ReverseProxy.Kestrel;

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
builder.Services.AddCertificateServices();

// 反向代理
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration)
    .LoadFromDatabase();

var app = builder.Build();

// 初始化证书并设置静态引用
await app.InitializeCertificatesAsync();

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
