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

app.UseRouting();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();
