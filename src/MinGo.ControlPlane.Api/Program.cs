using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using MinGo.Infrastructure.Data;
using MinGo.Application.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Services;
using Serilog;
using MinGo.ControlPlane.Api.GrpcServices;

Console.WriteLine("Starting MinGo Control Plane...");

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// 数据库
builder.Services.AddDbContext<ApiDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity (Cookie 认证)
builder.Services
    .AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
    Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme, options =>
{
    options.Cookie.Name = "MinGo.Auth";
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
    options.LoginPath = "/api/auth/login";
    options.LogoutPath = "/api/auth/logout";
    options.AccessDeniedPath = "/api/auth/denied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

builder.Services.Configure<Microsoft.AspNetCore.Identity.IdentityOptions>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
    options.User.RequireUniqueEmail = true;
});

builder.Services.AddAuthorization();

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
builder.Services.AddControllers();

// gRPC
builder.Services.AddGrpc();

// 业务服务
builder.Services.AddScoped<IApiDbService, ApiDbService>();
builder.Services.AddScoped<IMonitoringService, MonitoringService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IApiManagementService, ApiManagementService>();
builder.Services.AddScoped<IGatewayInstanceService, GatewayInstanceService>();
builder.Services.AddScoped<IGatewayEventSender, GatewayEventSender>();
builder.Services.AddScoped<IGatewayEventService, GatewayEventService>();
builder.Services.AddSingleton<IMessageNotificationService, MemoryMessageNotificationService>();
builder.Services.AddSingleton<TelemetryStore>();

// 数据面连接管理器
builder.Services.AddSingleton<DataPlaneConnectionManager>();
builder.Services.AddSingleton<ConfigReplicationService>();

// 配置变更 gRPC 广播
builder.Services.AddHostedService<MinGo.ControlPlane.Api.Services.ConfigUpdateGrpcBroadcaster>();

var app = builder.Build();

app.UseCors("Frontend");

// 自动迁移
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.Migrate();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<ConfigReplicationService>();
app.MapGrpcService<HeartbeatCollectService>();
app.MapGrpcService<EventSubscriptionService>();

app.Run();
