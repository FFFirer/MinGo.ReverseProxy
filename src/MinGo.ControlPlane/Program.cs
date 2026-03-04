using Microsoft.EntityFrameworkCore;
using MinGo.ControlPlane.Data;
using MinGo.ControlPlane.Services;
using Serilog;
using Vite.AspNetCore;

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
builder.Services.AddScoped<IConfigService, ConfigService>();
builder.Services.AddScoped<IMonitoringService, MonitoringService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IApiManagementService, ApiManagementService>();

// 注册配置更新服务
builder.Services.AddHostedService<ConfigUpdateService>();
builder.Services.AddHttpClient();

var app = builder.Build();

// 初始化数据库
using (var scope = app.Services.CreateScope())
{
    var gatewayDbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
    gatewayDbContext.Database.Migrate();
    
    var apiDbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    apiDbContext.Database.Migrate();
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

app.UseAntiforgery();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
