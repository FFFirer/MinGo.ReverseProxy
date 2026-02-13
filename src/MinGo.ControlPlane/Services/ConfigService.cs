using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Services;

public class ConfigService : IConfigService
{
    private readonly ILogger<ConfigService> _logger;
    private readonly List<GatewayConfig> _configs;

    public ConfigService(ILogger<ConfigService> logger)
    {
        _logger = logger;
        _configs = new List<GatewayConfig>();
    }

    public Task<GatewayConfig> GetConfigAsync()
    {
        var config = _configs.OrderByDescending(c => c.CreatedAt).FirstOrDefault() 
            ?? new GatewayConfig
            {
                Id = Guid.NewGuid().ToString(),
                Version = "1.0.0",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "System",
                Routes = new Dictionary<string, RouteConfig>(),
                Clusters = new Dictionary<string, ClusterConfig>(),
                Security = new SecurityConfig(),
                Monitoring = new MonitoringConfig()
            };
        return Task.FromResult(config);
    }

    public Task<GatewayConfig> GetConfigByVersionAsync(string version)
    {
        var config = _configs.FirstOrDefault(c => c.Version == version);
        return Task.FromResult(config ?? new GatewayConfig());
    }

    public Task<GatewayConfig> CreateConfigAsync(GatewayConfig config)
    {
        config.Id = Guid.NewGuid().ToString();
        config.Version = GenerateVersion();
        config.CreatedAt = DateTimeOffset.UtcNow;
        config.CreatedBy = "Admin";
        
        _configs.Add(config);
        _logger.LogInformation("Created new config: {ConfigId}, Version: {Version}", config.Id, config.Version);
        
        return Task.FromResult(config);
    }

    public Task<GatewayConfig> UpdateConfigAsync(string id, GatewayConfig config)
    {
        var existing = _configs.FirstOrDefault(c => c.Id == id);
        if (existing != null)
        {
            existing.Version = GenerateVersion();
            existing.Routes = config.Routes;
            existing.Clusters = config.Clusters;
            existing.Security = config.Security;
            existing.Monitoring = config.Monitoring;
            
            _logger.LogInformation("Updated config: {ConfigId}, Version: {Version}", existing.Id, existing.Version);
            return Task.FromResult(existing);
        }
        
        return Task.FromResult(new GatewayConfig());
    }

    public Task DeleteConfigAsync(string id)
    {
        var config = _configs.FirstOrDefault(c => c.Id == id);
        if (config != null)
        {
            _configs.Remove(config);
            _logger.LogInformation("Deleted config: {ConfigId}", id);
        }
        return Task.CompletedTask;
    }

    public Task NotifyConfigChangeAsync(GatewayConfig config)
    {
        _logger.LogInformation("Notifying config change: {ConfigId}, Version: {Version}", config.Id, config.Version);
        return Task.CompletedTask;
    }

    private string GenerateVersion()
    {
        return $"1.0.{_configs.Count + 1}";
    }
}
