using MinGo.ControlPlane.Data;
using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Services;

public class ConfigService : IConfigService
{
    private readonly ILogger<ConfigService> _logger;
    private readonly IDbConfigService _dbConfigService;

    public ConfigService(ILogger<ConfigService> logger, IDbConfigService dbConfigService)
    {
        _logger = logger;
        _dbConfigService = dbConfigService;
    }

    public Task<GatewayConfig> GetConfigAsync()
    {
        return _dbConfigService.GetLatestConfigAsync();
    }

    public Task<GatewayConfig> GetConfigByVersionAsync(string version)
    {
        return _dbConfigService.GetConfigByVersionAsync(version);
    }

    public Task<GatewayConfig> CreateConfigAsync(GatewayConfig config)
    {
        return _dbConfigService.CreateConfigAsync(config);
    }

    public Task<GatewayConfig> UpdateConfigAsync(string id, GatewayConfig config)
    {
        return _dbConfigService.UpdateConfigAsync(id, config);
    }

    public Task DeleteConfigAsync(string id)
    {
        return _dbConfigService.DeleteConfigAsync(id);
    }

    public Task NotifyConfigChangeAsync(GatewayConfig config)
    {
        _logger.LogInformation("Notifying config change: {ConfigId}, Version: {Version}", config.Id, config.Version);
        return Task.CompletedTask;
    }
}
