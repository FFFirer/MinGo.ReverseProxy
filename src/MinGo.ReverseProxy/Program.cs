using Microsoft.EntityFrameworkCore;
using MinGo.Infrastructure.Data;
using MinGo.Application.Services;
using Serilog;
using Vite.AspNetCore;

Console.WriteLine("Starting MinGo Reverse Proxy...");

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

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
    .LoadFromConfig(builder.Configuration);


var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if(app.Environment.IsDevelopment())
{
    app.UseViteDevelopmentServer(true);
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

