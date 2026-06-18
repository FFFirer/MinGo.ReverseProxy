using MinGo.Core.Logging;
using MinGo.Core.Services;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Heartbeat;
using MinGo.DataPlane.Telemetry;
using MinGo.DataPlane.Kestrel;
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

var app = builder.Build();

// 启动配置同步（等待首次配置就绪）
var configSync = app.Services.GetRequiredService<ConfigSyncService>();
await configSync.StartAsync(CancellationToken.None);
// 等待首次配置到达
await configSync.WaitForInitialConfigAsync(TimeSpan.FromSeconds(30));

app.UseGatewayTelemetry();
app.UseSerilogRequestLogging();
app.MapReverseProxy();

// 启动事件订阅后台服务（响应 CONFIG_QUERY）
_ = app.Services.GetRequiredService<ConfigQueryHandler>().StartAsync(CancellationToken.None);

// 启动心跳后台服务
_ = app.Services.GetRequiredService<HeartbeatReporter>().StartAsync(CancellationToken.None);

app.Run();
