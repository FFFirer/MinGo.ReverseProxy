using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Services;
using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly IConfigService _configService;
    private readonly ILogger<ConfigController> _logger;

    public ConfigController(IConfigService configService, ILogger<ConfigController> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<GatewayConfig>> GetConfig()
    {
        var config = await _configService.GetConfigAsync();
        return Ok(config);
    }

    [HttpGet("{version}")]
    public async Task<ActionResult<GatewayConfig>> GetConfigByVersion(string version)
    {
        var config = await _configService.GetConfigByVersionAsync(version);
        if (config == null)
        {
            return NotFound();
        }
        return Ok(config);
    }

    [HttpPost]
    public async Task<ActionResult<GatewayConfig>> CreateConfig([FromBody] GatewayConfig config)
    {
        var created = await _configService.CreateConfigAsync(config);
        return CreatedAtAction(nameof(GetConfig), new { version = created.Version }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<GatewayConfig>> UpdateConfig(string id, [FromBody] GatewayConfig config)
    {
        var updated = await _configService.UpdateConfigAsync(id, config);
        if (updated == null)
        {
            return NotFound();
        }
        
        await _configService.NotifyConfigChangeAsync(updated);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteConfig(string id)
    {
        await _configService.DeleteConfigAsync(id);
        return NoContent();
    }
}
