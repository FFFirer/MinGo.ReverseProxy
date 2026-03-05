using Microsoft.EntityFrameworkCore;
using MinGo.ControlPlane.Data;
using MinGo.ControlPlane.Services;
using MinGo.ControlPlane.Options;
using MinGo.ControlPlane.Extensions;
using Serilog;
using Vite.AspNetCore;
using Yarp.ReverseProxy.Configuration;

Console.WriteLine("Starting MinGo Control Plane...");

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

// 配置反向代理，使用数据库配置提供程序
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration)
    .LoadFromDatabase();

// 添加健康检查服务
builder.Services.AddHealthChecks();

// 配置ControlPlane选项
builder.Services.Configure<ControlPlaneOptions>(
    builder.Configuration.GetSection(ControlPlaneOptions.SectionName));

// 添加HTTP客户端工厂，配置ControlPlane客户端
builder.Services.AddHttpClient("ControlPlane", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ControlPlaneOptions>>().Value;
    client.BaseAddress = new Uri(options.Url);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
})
.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ControlPlaneOptions>>().Value;
    var handler = new System.Net.Http.HttpClientHandler();

    if (options.SkipSslCertificateValidation)
    {
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
    }

    return handler;
});

// 配置数据库
builder.Services.AddDbContext<GatewayDbContext>(options =>
    options.UseSqlite("Data Source=gateway.db")
);
builder.Services.AddDbContext<ApiDbContext>(options =>
    options.UseSqlite("Data Source=gateway.db")
);

// 注册服务
builder.Services.AddScoped<IDbConfigService, DbConfigService>();
builder.Services.AddScoped<IApiDbService, ApiDbService>();
builder.Services.AddScoped<IMonitoringService, MonitoringService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IApiManagementService, ApiManagementService>();
builder.Services.AddScoped<IGatewayInstanceService, GatewayInstanceService>();
// 注册事件服务
builder.Services.AddScoped<IGatewayEventSender, HttpGatewayEventSender>();
builder.Services.AddScoped<IGatewayEventService, GatewayEventService>();

// 注册配置更新服务
builder.Services.AddHostedService<GatewayInstanceHealthCheckService>();
builder.Services.AddHostedService<MinGo.ControlPlane.Services.GatewayInstanceRegistrationService>();
builder.Services.AddHttpClient();

var app = builder.Build();

// 初始化数据库和示例数据
// await InitializeDatabaseAsync(app);

async Task InitializeDatabaseAsync(WebApplication app)
{
    using (var scope = app.Services.CreateScope())
    {
        var gatewayDbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        gatewayDbContext.Database.Migrate();
        
        var apiDbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        apiDbContext.Database.Migrate();
        
        // 初始化示例数据
        var apiDbService = scope.ServiceProvider.GetRequiredService<IApiDbService>();
        await apiDbService.InitializeSampleDataAsync();
        Console.WriteLine("Sample data initialized");
    }
}


Console.WriteLine("Configuring middleware...");

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

// 映射健康检查端点
app.MapHealthChecks("/health");

// 映射反向代理端点（所有非管理路径）
app.MapReverseProxy();

// 映射管理界面和API
app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
