using MinGo.Core.Services;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Heartbeat;
using MinGo.DataPlane.Telemetry;
using MinGo.DataPlane.Kestrel;

Console.WriteLine("Starting MinGo Data Plane...");

var builder = WebApplication.CreateBuilder(args);

// 配置端口 - 从环境变量或配置读取
var proxyPorts = builder.Configuration.GetSection("ProxyPorts").Get<int[]>() ?? [8080];

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

app.MapWhen(ctx => proxyPorts.Contains(ctx.Connection.LocalPort), proxyApp =>
{
    proxyApp.UseGatewayTelemetry();
    proxyApp.UseRouting();
    proxyApp.UseEndpoints(endpoints =>
    {
        endpoints.MapReverseProxy();
    });
});

// 启动心跳后台服务
_ = app.Services.GetRequiredService<HeartbeatReporter>().StartAsync(CancellationToken.None);

app.Run();
