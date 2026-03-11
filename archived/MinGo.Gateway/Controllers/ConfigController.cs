using Microsoft.AspNetCore.Mvc;
using MinGo.Shared.Models;
using MinGo.Gateway.Services;
using Yarp.ReverseProxy.Configuration;

namespace MinGo.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly ILogger<ConfigController> _logger;
    private readonly DatabaseProxyConfigProvider _configProvider;

    public ConfigController(ILogger<ConfigController> logger, DatabaseProxyConfigProvider configProvider)
    {
        _logger = logger;
        _configProvider = configProvider;
    }

    /// <summary>
    /// 获取网关状态
    /// </summary>
    /// <returns>状态信息</returns>
    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            Status = "Running",
            Version = "1.0.0",
            Timestamp = DateTimeOffset.UtcNow,
            ConfigSource = "Database"
        });
    }

    /// <summary>
    /// 获取网关健康状态
    /// </summary>
    /// <returns>健康状态信息</returns>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// 手动触发配置重新加载
    /// </summary>
    /// <returns>重载结果</returns>
    [HttpPost("reload")]
    public IActionResult ReloadConfig()
    {
        try
        {
            _configProvider.Refresh();
            _logger.LogInformation("Manually triggered config refresh from control plane");
            
            return Ok(new
            {
                Status = "Success",
                Message = "Config refreshed from control plane successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh config");
            return StatusCode(500, new
            {
                Status = "Error",
                Message = "Failed to refresh config"
            });
        }
    }

    /// <summary>
    /// 获取当前YARP配置
    /// </summary>
    /// <returns>当前YARP配置</returns>
    [HttpGet("current")]
    public IActionResult GetCurrentConfig()
    {
        try
        {
            var config = _configProvider.GetConfig();
            return Ok(new
            {
                Routes = config.Routes,
                Clusters = config.Clusters,
                ChangeTime = ((MinGo.Gateway.Services.DatabaseProxyConfig)config).ChangeTime
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current config");
            return StatusCode(500, new
            {
                Status = "Error",
                Message = "Failed to get current config"
            });
        }
    }
}
