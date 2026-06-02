using Microsoft.EntityFrameworkCore;
using MinGo.Infrastructure.Data;
using MinGo.Application.Services;
using Serilog;
using Vite.AspNetCore;
using MinGo.Infrastructure;
using MinGo.Core.Services;
using MinGo.Core.Interfaces;
using MinGo.Infrastructure.ExternalServices;
using System.Security.Cryptography.X509Certificates;
using MinGo.ReverseProxy.Kestrel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Security.Claims;
using MinGo.ReverseProxy.Services;
using Microsoft.AspNetCore.DataProtection;

Console.WriteLine("Starting MinGo Reverse Proxy...");

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

int[] AdminPorts = builder.Configuration.GetSection(nameof(AdminPorts)).Get<int[]>() ?? [];
int[] ProxyPorts = builder.Configuration.GetSection(nameof(ProxyPorts)).Get<int[]>() ?? [];

builder.Host.UseSerilog();

// 配置 Data Protection（用于 Cookie 加密）
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "..", "keys")));

builder.Services.AddViteServices();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddControllers();
builder.Services.AddNamedHttpClients(builder.Configuration);

// 注册 AuthService - 基于 ASP.NET Core Identity Cookie 认证
builder.Services.AddScoped<AuthService>();
builder.Services.AddCascadingAuthenticationState();

// 配置数据库
builder.Services.AddDbContext<ApiDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// 配置 Identity (Cookie 认证方案 - 适合 Blazor Server)
// 使用现有 Blazor 登录/注册页面，不需要 AddDefaultUI()
builder.Services
    .AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// 配置 Cookie 认证选项
builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(options =>
{
    options.LoginPath = "/Login";
    options.LogoutPath = "/Logout";
    options.AccessDeniedPath = "/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

// 配置 Identity 选项
builder.Services.Configure<IdentityOptions>(options =>
{
    // Password settings - 宽松配置便于开发
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
    
    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
    
    // User settings
    options.User.RequireUniqueEmail = true;
});

builder.Services.AddAuthorization();

// 注册遥测存储
builder.Services.AddSingleton<TelemetryStore>();

// 注册服务
builder.Services.AddScoped<IApiDbService, MinGo.Infrastructure.Data.ApiDbService>();
builder.Services.AddScoped<IMonitoringService, MinGo.Application.Services.MonitoringService>();
builder.Services.AddScoped<ILogService, MinGo.Application.Services.LogService>();
builder.Services.AddScoped<IApiManagementService, MinGo.Application.Services.ApiManagementService>();
builder.Services.AddScoped<IGatewayInstanceService, MinGo.Application.Services.GatewayInstanceService>();
builder.Services.AddScoped<IGatewayEventSender, MinGo.Application.Services.GatewayEventSender>();
builder.Services.AddScoped<IGatewayEventService, MinGo.Application.Services.GatewayEventService>();
builder.Services.AddSingleton<IMessageNotificationService, MinGo.Application.Services.MemoryMessageNotificationService>();

// 注册配置更新事件监听器
builder.Services.AddHostedService<MinGo.Infrastructure.ExternalServices.ConfigUpdateEventListener>();

// 注册证书管理器
builder.Services.AddCertificateServices();

// 反向代理
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration)
    .LoadFromDatabase();

var app = builder.Build();

// 初始化证书并设置静态引用
await app.InitializeCertificatesAsync();

// 从数据库加载配置（同步等待，避免启动后空配置竞争条件）
await app.InitializeDatabaseProxyConfigAsync();

app.UseDevelopmentAutoMigration();

if(!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseGatewayTelemetry();

// 添加 Cookie 策略中间件
app.UseCookiePolicy();

app.MapWhen(x => AdminPorts.Contains(x.Connection.LocalPort), b =>
{
    if (app.Environment.IsDevelopment())
    {
        b.UseViteDevelopmentServer(true);
    }

    b.UseStaticFiles();

    b.UseRouting();
    b.UseAuthentication();
    b.UseAuthorization();

    b.UseEndpoints(e =>
    {
        e.MapControllers();
        e.MapBlazorHub();
        e.MapFallbackToPage("/_Host");
    });
});

app.MapWhen(x => ProxyPorts.Contains(x.Connection.LocalPort), p =>
{
    p.UseRouting();
    p.UseEndpoints(e =>
    {
        e.MapReverseProxy();
    });
});

app.Run();

/// <summary>
/// 证书选择器静态持有器
/// 用于在 Kestrel TLS 回调中访问 CertificateManager 和 Fallback 逻辑
/// </summary>
internal static class CertificateSelector
{
    /// <summary>
    /// 证书管理器实例
    /// </summary>
    public static ICertificateManager? CertificateManager { get; set; }
    
    /// <summary>
    /// Fallback 证书选择器（由 ConfigureHttpsDefaults 设置的默认逻辑）
    /// </summary>
    public static Func<Microsoft.AspNetCore.Connections.ConnectionContext, string, X509Certificate2?>? FallbackSelector { get; set; }
}
