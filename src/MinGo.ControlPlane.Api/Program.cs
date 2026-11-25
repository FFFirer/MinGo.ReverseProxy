using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MinGo.Infrastructure.Data;
using MinGo.Application.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Logging;
using MinGo.Core.Services;
using MinGo.ControlPlane.Api.Configuration;
using MinGo.ControlPlane.Api.Data;
using MinGo.ControlPlane.Api.GrpcServices;
using MinGo.ControlPlane.Api.Services;
using MinGo.IdentityKit;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System.Text.Json.Serialization;

Console.WriteLine("Starting MinGo Control Plane...");

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>();

// Serilog — 共享配置
builder.Host.UseSerilog(SerilogSetup.ConfigureSharedSerilog());

// NSwag
builder.Services.AddOpenApiDocument().AddEndpointsApiExplorer();

// 数据库（统一 AppDbContext）
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity (Cookie 认证)
builder.Services
    .AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddPasswordResetAdministration(options =>
        options.AuthorizationPolicy = "ManageAccountPasswords");

// 部署方显式指定账号 ID，空列表默认拒绝全部账号。
var passwordAdministrators = builder.Configuration
    .GetSection("AccountAdministration:UserIds").Get<string[]>() ?? [];
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ManageAccountPasswords", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context => passwordAdministrators.Contains(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value)));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "MinGo.Auth";
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
    // 保留现有 HTTP 开发入口和 HTTPS 请求的 Cookie 行为。
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.LoginPath = "/api/auth/login";
    options.LogoutPath = "/api/auth/logout";
    options.AccessDeniedPath = "/api/auth/denied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

builder.Services.Configure<Microsoft.AspNetCore.Identity.IdentityOptions>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    options.User.RequireUniqueEmail = true;
});

// 健康检查（就绪检查：EF Core DbContext）
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", HealthStatus.Unhealthy, tags: ["ready"]);

// CORS - SolidJS 前端跨域
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(
                builder.Configuration["Frontend:Url"] ?? "http://localhost:5173")
              .AllowCredentials()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// Controllers
// 枚举按字符串序列化：前端按 'Online'/'HeartbeatTimeout' 等字符串消费实例状态，数字会把所有状态打进 default
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// gRPC
// DataPlaneAuthInterceptor：配置 DataPlane:SharedSecrets 后校验所有数据面 gRPC 连接，
// 未配置时直通（本地/单机部署行为不变）
builder.Services.AddSingleton<DataPlaneAuthInterceptor>();
builder.Services.AddGrpc(o => o.Interceptors.Add<DataPlaneAuthInterceptor>());

// 业务服务
builder.Services.AddScoped<IApiDbService, ApiDbService>();
builder.Services.AddScoped<IMonitoringService, MonitoringService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IApiManagementService, ApiManagementService>();
builder.Services.AddSingleton<IGatewayInstanceService, GatewayInstanceService>();
builder.Services.AddScoped<IGatewayEventSender, GatewayEventSender>();
builder.Services.AddScoped<IGatewayEventService, GatewayEventService>();
builder.Services.AddSingleton<IMessageNotificationService, MemoryMessageNotificationService>();
builder.Services.AddSingleton<TelemetryStore>();

// 数据面连接管理器
builder.Services.AddSingleton<DataPlaneConnectionManager>();
builder.Services.AddSingleton<ConfigReplicationService>();

// 实例配置查询服务（通过 EventSubscription 通道）
builder.Services.AddSingleton<InstanceConfigQueryService>();

// 事件订阅服务（同时作为 gRPC endpoint 和 DI 服务）
builder.Services.AddSingleton<EventSubscriptionService>();

// 配置变更 gRPC 广播
builder.Services.AddHostedService<MinGo.ControlPlane.Api.Services.ConfigUpdateGrpcBroadcaster>();

// Portainer 多实例管理（DB 存储 + 页面 CRUD）
builder.Services.Configure<PortainerOptions>(builder.Configuration.GetSection(PortainerOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IPortainerInstanceService, PortainerInstanceService>();
builder.Services.AddSingleton<PortainerClientFactory>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<PortainerDiscoveryService>();

// Cloudflare Tunnels（接入配置在设置页管理并落库，配置节仅作默认值兜底）
builder.Services.Configure<CloudflareOptions>(builder.Configuration.GetSection(CloudflareOptions.SectionName));
builder.Services.AddHttpClient("Cloudflare", client =>
{
    client.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<ICloudflareSettingsService, CloudflareSettingsService>();
builder.Services.AddScoped<CloudflareConfigProvider>();
builder.Services.AddScoped<CloudflareClient>();
builder.Services.AddScoped<CloudflareSyncService>();

// 网络拓扑聚合（只读：图 + 配置一致性体检）
builder.Services.AddScoped<TopologyService>();

// OpenTelemetry (可通过 OpenTelemetry:Enabled=false 禁用)
var otelSection = builder.Configuration.GetSection("OpenTelemetry");
if (otelSection.GetValue<bool>("Enabled", true))
{
    var otelEndpoint = otelSection["OtlpEndpoint"] ?? "http://localhost:4317";
    var serviceName = otelSection["ServiceName"] ?? "min-go-control-plane";
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi();
    app.UseCors("Frontend");
}
else
{
    // 缓存策略：index.html 及 SPA 回退页禁止缓存，保证发版后客户端立即拿到新入口；
    // 带内容哈希的静态资源（/assets/*）长期缓存。
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        context.Response.OnStarting(() =>
        {
            var response = context.Response;
            if (path.StartsWithSegments("/assets"))
            {
                response.Headers.CacheControl = "public, max-age=31536000, immutable";
            }
            else if (response.ContentType?.StartsWith("text/html") == true)
            {
                response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                response.Headers.Pragma = "no-cache";
                response.Headers.Expires = "0";
            }
            return Task.CompletedTask;
        });
        await next();
    });

    // 生产环境：从 wwwroot 提供前端静态文件
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    sp.GetRequiredService<AppDbContext>().Database.Migrate();

    // 种子数据：首次运行时创建默认管理员（所有环境）
    await DbInitializer.SeedDataAsync(sp, app.Configuration);
}

// 连接配置查询服务与事件订阅服务（所有环境均需装配）
var configQueryService = app.Services.GetRequiredService<InstanceConfigQueryService>();
var eventSubService = app.Services.GetRequiredService<EventSubscriptionService>();
configQueryService.TrySendEventAsync = (instanceId, eventMsg) =>
    eventSubService.TrySendEventAsync(instanceId, eventMsg);

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<ConfigReplicationService>();
app.MapGrpcService<HeartbeatCollectService>();
app.MapGrpcService<EventSubscriptionService>();

// 版本端点：前端用于检测新版本
var appVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "unknown";
app.MapGet("/api/version", () => Results.Json(new { version = appVersion }));

// 健康检查端点
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = _ => false // 存活检查：不运行任何检查，仅返回 200
});
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    }
});

// SPA 回退：必须放在所有路由映射之后，防止吞掉 API/gRPC 404
if (!app.Environment.IsDevelopment())
{
    // API 路径未命中任何路由时返回 404 JSON，避免 SPA 回退返回 HTML
    // 导致前端 JSON 解析报 "Unexpected token '<'" 这类难以定位的错误
    app.MapFallback("/api/{**path}", (HttpContext context) =>
        Results.Json(
            new
            {
                error = "NotFound",
                message = $"API endpoint not found: {context.Request.Method} {context.Request.Path}",
            },
            statusCode: StatusCodes.Status404NotFound));

    app.MapFallbackToFile("index.html");
}

app.Run();
