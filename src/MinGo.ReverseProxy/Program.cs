using Microsoft.EntityFrameworkCore;
using MinGo.Infrastructure.Data;
using MinGo.Application.Services;
using Serilog;
using Vite.AspNetCore;
using MinGo.Infrastructure;
using MinGo.Core.Services;
using MinGo.Infrastructure.ExternalServices;

Console.WriteLine("Starting MinGo Reverse Proxy...");

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

int[] AdminPorts = builder.Configuration.GetSection(nameof(AdminPorts)).Get<int[]>() ?? [];
int[] ProxyPorts = builder.Configuration.GetSection(nameof(ProxyPorts)).Get<int[]>() ?? [];

var adminHosts = AdminPorts.Select(p => $"*:{p}").ToArray();
var proxyHosts = ProxyPorts.Select(p => $"*:{p}").ToArray();

builder.Host.UseSerilog();
builder.Services.AddViteServices();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddControllers();
builder.Services.AddHttpClient();

// 配置数据库
builder.Services.AddDbContext<ApiDbContext>(options =>
    options.UseSqlite("Data Source=proxy.db")
);

// 注册服务
builder.Services.AddScoped<MinGo.Core.Interfaces.IApiDbService, MinGo.Infrastructure.Data.ApiDbService>();
builder.Services.AddScoped<MinGo.Core.Interfaces.IMonitoringService, MinGo.Application.Services.MonitoringService>();
builder.Services.AddScoped<MinGo.Core.Interfaces.ILogService, MinGo.Application.Services.LogService>();
builder.Services.AddScoped<MinGo.Core.Interfaces.IApiManagementService, MinGo.Application.Services.ApiManagementService>();
builder.Services.AddScoped<MinGo.Core.Interfaces.IGatewayInstanceService, MinGo.Application.Services.GatewayInstanceService>();
builder.Services.AddScoped<MinGo.Core.Interfaces.IGatewayEventSender, MinGo.Application.Services.GatewayEventSender>();
builder.Services.AddScoped<MinGo.Core.Interfaces.IGatewayEventService, MinGo.Application.Services.GatewayEventService>();
builder.Services.AddSingleton<MinGo.Core.Interfaces.IMessageNotificationService, MinGo.Application.Services.MemoryMessageNotificationService>();

// 注册遥测存储
builder.Services.AddSingleton<TelemetryStore>();

// 注册配置更新事件监听器
builder.Services.AddHostedService<MinGo.Infrastructure.ExternalServices.ConfigUpdateEventListener>();

// 反向代理
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration)
    .LoadFromDatabase();

var app = builder.Build();

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

    // b.UseHttpsRedirection();

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

