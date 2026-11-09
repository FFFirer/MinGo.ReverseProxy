using System.Net;
using MinGo.Core.Logging;
using MinGo.Core.Services;
using MinGo.DataPlane;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Heartbeat;
using MinGo.DataPlane.Kestrel;
using MinGo.DataPlane.Middleware;
using MinGo.DataPlane.Telemetry;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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

// 后台服务：配置同步、事件订阅、心跳上报
// GatewayIdentity 为所有服务提供统一的 DataPlaneId，程序启动时即就绪
builder.Services.AddSingleton<GatewayIdentity>();
builder.Services.AddHostedService<ConfigSyncService>();
builder.Services.AddHostedService<ConfigQueryHandler>();
builder.Services.AddHostedService<HeartbeatReporter>();

// YARP 反向代理 - 从 DataPlaneConfigProvider 加载配置
// YARP 默认会添加 X-Forwarded-For/Proto/Host 头，无需额外配置
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .LoadFromDataPlaneProvider();

// Kestrel 证书选择
builder.Services.AddCertificateServices();

// HTTP → HTTPS 跳转（默认开启；Development 与本地 compose 关闭）
var httpsRedirectOptions = builder.Configuration
    .GetSection(HttpsRedirectOptions.SectionName)
    .Get<HttpsRedirectOptions>() ?? new HttpsRedirectOptions();

// 内置中间件在端口未知时 fail-open（放行明文 + warning），这里改为启动即失败
httpsRedirectOptions.Validate();

builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = httpsRedirectOptions.Port;
    options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
});

// ForwardedHeaders — 前置 TLS 终结层（Cloudflare Tunnel 等）场景
// 解析 X-Forwarded-For/Proto/Host，覆盖 HttpContext 使 Request.IsHttps 正确
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost;

    // 清空 KnownProxies/KnownIPNetworks = 信任所有来源的转发头
    // 安全约束：仅在数据面前有可信代理（如 Cloudflare Tunnel）时启用
    // 若需限制来源，在 ForwardedHeaders:KnownProxies 配置信任 IP 列表
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    // 从配置读取信任网络（支持 KnownProxies / KnownNetworks / ForwardLimit）
    var fhSection = builder.Configuration.GetSection("ForwardedHeaders");
    var knownProxies = fhSection.GetSection("KnownProxies").Get<string[]>() ?? [];
    foreach (var proxy in knownProxies)
    {
        if (IPAddress.TryParse(proxy, out var ip))
            options.KnownProxies.Add(ip);
    }

    var knownNetworks = fhSection.GetSection("KnownNetworks").Get<string[]>() ?? [];
    foreach (var network in knownNetworks)
    {
        var parts = network.Split('/');
        if (parts.Length == 2
            && IPAddress.TryParse(parts[0], out var netAddr)
            && int.TryParse(parts[1], out var prefixLen))
        {
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(netAddr, prefixLen));
        }
    }

    var forwardLimit = fhSection.GetValue<int?>("ForwardLimit");
    if (forwardLimit.HasValue)
        options.ForwardLimit = forwardLimit.Value;
});

// OpenTelemetry (可通过 OpenTelemetry:Enabled=false 禁用)
var otelSection = builder.Configuration.GetSection("OpenTelemetry");
if (otelSection.GetValue<bool>("Enabled", true))
{
    var otelEndpoint = otelSection["OtlpEndpoint"] ?? "http://localhost:4317";
    var serviceName = otelSection["ServiceName"] ?? "min-go-data-plane";
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName))
        .WithTracing(tracing => tracing
            .AddSource("Gateway")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)))
        .WithMetrics(metrics => metrics
            .AddMeter("Gateway")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));
}

// 健康检查
builder.Services.AddHealthChecks();

var app = builder.Build();

// 健康检查端点
var configProvider = app.Services.GetRequiredService<DataPlaneConfigProvider>();
var certificateSelector = app.Services.GetRequiredService<DataPlaneCertificateSelector>();
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

// ForwardedHeaders 必须在 HTTPS 跳转判定之前执行，
// 使 X-Forwarded-Proto 覆盖 Request.Scheme，避免前置 TLS 终结场景下的重定向死循环
app.UseForwardedHeaders();

// 明文 → HTTPS：准入 = 域名证书命中（不回退默认证书）；健康检查、localhost、IP 直连豁免。
// 必须位于 UseRouting 之前 —— 实测端点注册不会提前吞掉请求
if (httpsRedirectOptions.Enabled)
{
    app.MapWhen(
        context => HttpsRedirectPolicy.NeedsRedirect(context, httpsRedirectOptions, certificateSelector),
        branch => branch.UseHttpsRedirection());
}

app.UseRouting();
app.UseGatewayTelemetry();
app.UseSerilogRequestLogging();
app.MapReverseProxy();

app.Run();
