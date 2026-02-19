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

builder.Services.AddScoped<IConfigService, ConfigService>();
builder.Services.AddScoped<IMonitoringService, MonitoringService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IApiManagementService, ApiManagementService>();

var app = builder.Build();

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
