using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Options;
using MinGo.Gateway.Options;
using MinGo.Gateway.Services;
using MinGo.Gateway.Extensions;
using MinGo.Shared.Data;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
.ReadFrom.Configuration(builder.Configuration)
.CreateLogger();

builder.Host.UseSerilog();

// 配置数据库 - 使用与控制平面相同的数据库
var connectionString = builder.Configuration.GetConnectionString("ProxyDb") 
    ?? "Data Source=gateway.db";
builder.Services.AddDbContext<ProxyDbContext>(options =>
    options.UseSqlite(connectionString));

// 配置反向代理，使用数据库配置提供程序
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration)
    .LoadFromDatabase();

builder.Services.AddHealthChecks();
builder.Services.AddControllers();

// 配置ControlPlane选项
builder.Services.Configure<ControlPlaneOptions>(
    builder.Configuration.GetSection(ControlPlaneOptions.SectionName));

// 添加HTTP客户端工厂，配置ControlPlane客户端
builder.Services.AddHttpClient("ControlPlane", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ControlPlaneOptions>>().Value;
    client.BaseAddress = new Uri(options.Url);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
})
.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ControlPlaneOptions>>().Value;
    var handler = new HttpClientHandler();

    if (options.SkipSslCertificateValidation)
    {
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
    }

    return handler;
});

// 注册Gateway实例服务
builder.Services.AddHostedService<GatewayInstanceRegistrationService>();

var app = builder.Build();

// 初始化数据库
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
    dbContext.Database.EnsureCreated();
}

app.UseRouting();

app.UseEndpoints(endpoints =>
{
    // 为管理接口配置路由（使用 8081/8444 端口）
    endpoints.MapControllers().RequireHost("*:8081", "*:8444");
    endpoints.MapHealthChecks("/health").RequireHost("*:8081", "*:8444");
    
    // 为反向代理配置路由（使用 8080/8443 端口）
    endpoints.MapReverseProxy().RequireHost("*:8080", "*:8443");
});

app.Run();
