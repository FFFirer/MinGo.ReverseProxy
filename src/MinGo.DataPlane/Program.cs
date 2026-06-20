using MinGo.Core.Logging;
using MinGo.Core.Services;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Heartbeat;
using MinGo.DataPlane.Telemetry;
using MinGo.DataPlane.Kestrel;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

Console.WriteLine("Starting MinGo Data Plane...");

var builder = WebApplication.CreateBuilder(args);

// Serilog — 共享配置
builder.Host.UseSerilog(SerilogSetup.ConfigureSharedSerilog());

// gRPC 客户端 - 连接控制面
var controlPlaneGrpcUrl = builder.Configuration["ControlPlane:GrpcUrl"] ?? "http://localhost:5001";
builder.Services.AddGrpcClient<MinGo.DataPlane.Grpc.ConfigReplication.ConfigReplicationClient>(o =>
    o.Address = new Uri(controlPlaneGrpcUrl));
builder.Services.AddGrpcClient<MinGo.DataPlane.Grpc.HeartbeatCollect.HeartbeatCollectClient>(o =>
    o.Address = new Uri(controlPlaneGrpcUrl));
builder.Services.AddGrpcClient<MinGo.DataPlane.Grpc.EventSubscription.EventSubscriptionClient>(o =>
    o.Address = new Uri(controlPlaneGrpcUrl));

// 遥测存储
builder.Services.AddSingleton<TelemetryStore>();

// 配置同步服务
builder.Services.AddSingleton<DataPlaneConfigProvider>();
builder.Services.AddSingleton<ConfigSyncService>();

// 配置查询处理器（响应控制面的 CONFIG_QUERY 事件）
builder.Services.AddSingleton<ConfigQueryHandler>();

// 心跳上报服务
builder.Services.AddSingleton<HeartbeatReporter>();

// YARP 反向代理 - 从 DataPlaneConfigProvider 加载配置
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration)
    .LoadFromDataPlaneProvider();

// Kestrel 证书选择
builder.Services.AddCertificateServices();

// 健康检查
builder.Services.AddHealthChecks();

var app = builder.Build();

// 启动配置同步（不阻塞，配置异步到达后热加载）
var configSync = app.Services.GetRequiredService<ConfigSyncService>();
_ = configSync.StartAsync(CancellationToken.None);

app.UseGatewayTelemetry();
app.UseSerilogRequestLogging();
app.MapReverseProxy();

// 健康检查端点
var configProvider = app.Services.GetRequiredService<DataPlaneConfigProvider>();

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = _ => false // 存活检查：不运行任何检查，仅返回 200
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var status = configProvider.LastApplySucceeded ? "Healthy" : "Unhealthy";
        context.Response.StatusCode = configProvider.LastApplySucceeded ? 200 : 503;
        await context.Response.WriteAsync($"{{\"status\":\"{status}\"}}");
    }
});

// 启动事件订阅后台服务（响应 CONFIG_QUERY）
_ = app.Services.GetRequiredService<ConfigQueryHandler>().StartAsync(CancellationToken.None);

// 启动心跳后台服务
_ = app.Services.GetRequiredService<HeartbeatReporter>().StartAsync(CancellationToken.None);

app.Run();
