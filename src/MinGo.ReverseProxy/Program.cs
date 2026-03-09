using Microsoft.EntityFrameworkCore;
using MinGo.Infrastructure.Data;
using MinGo.Application.Services;
using Serilog;
using Vite.AspNetCore;
using MinGo.Infrastructure;

Console.WriteLine("Starting MinGo Reverse Proxy...");

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

int[] AdminPorts = [58074];
int[] ProxyPorts = [5286, 7064];

// builder.Configuration.GetSection(nameof(AdminPorts)).Bind(AdminPorts);
// builder.Configuration.GetSection(nameof(ProxyPorts)).Bind(ProxyPorts);

builder.Host.UseSerilog();
builder.Services.AddViteServices();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddControllers();
builder.Services.AddHttpClient();

// 配置数据库
builder.Services.AddDbContext<ApiDbContext>(options =>
    options.UseSqlite("Data Source=gateway.db")
);

// 注册服务
builder.Services.AddScoped<MinGo.Core.Interfaces.IApiDbService, MinGo.Infrastructure.Data.ApiDbService>();
builder.Services.AddScoped<MinGo.Core.Interfaces.IMonitoringService, MinGo.Application.Services.MonitoringService>();
builder.Services.AddScoped<MinGo.Core.Interfaces.ILogService, MinGo.Application.Services.LogService>();
builder.Services.AddScoped<MinGo.Core.Interfaces.IApiManagementService, MinGo.Application.Services.ApiManagementService>();
builder.Services.AddScoped<MinGo.Core.Interfaces.IGatewayInstanceService, MinGo.Application.Services.GatewayInstanceService>();
builder.Services.AddScoped<MinGo.Core.Interfaces.IGatewayEventSender, MinGo.Application.Services.GatewayEventSender>();
builder.Services.AddScoped<MinGo.Core.Interfaces.IGatewayEventService, MinGo.Application.Services.GatewayEventService>();

// 反向代理
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration)
    .LoadFromDatabase();

var app = builder.Build();

app.UseDevelopmentAutoMigration();

app.MapWhen(x => AdminPorts.Contains(x.Connection.LocalPort), b =>
{
    if (!app.Environment.IsDevelopment())
    {
        b.UseExceptionHandler("/Error");
        // b.UseHsts();
    }

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

app.MapWhen(x => ProxyPorts.Contains(x.Connection.LocalPort), b =>
{
    b.UseRouting();
    b.UseEndpoints(e =>
    {
        e.MapReverseProxy();
    });
});

var adminHosts = AdminPorts.Select(p => $"*:{p}").ToArray();
var proxyHosts = ProxyPorts.Select(p => $"*:{p}").ToArray();

// app.MapControllers().RequireHost(adminHosts);
// app.MapBlazorHub().RequireHost(adminHosts);
// app.MapFallbackToPage("/_Host").RequireHost(adminHosts);
// app.MapReverseProxy().RequireHost(proxyHosts);

app.Run();

