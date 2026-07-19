using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Entities;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InstancesController : ControllerBase
{
    private readonly IGatewayInstanceService _gatewayInstanceService;
    private readonly InstanceConfigQueryService _configQueryService;
    private readonly ILogger<InstancesController> _logger;

    public InstancesController(
        IGatewayInstanceService gatewayInstanceService,
        InstanceConfigQueryService configQueryService,
        ILogger<InstancesController> logger)
    {
        _gatewayInstanceService = gatewayInstanceService;
        _configQueryService = configQueryService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<GatewayInstanceListResponse>> GetInstances()
    {
        var instances = await _gatewayInstanceService.GetInstancesAsync();
        return Ok(instances);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GatewayInstance>> GetInstance(string id)
    {
        var instance = await _gatewayInstanceService.GetInstanceAsync(id);
        if (instance == null) return NotFound();
        return Ok(instance);
    }

    [HttpPost("register")]
    public async Task<ActionResult<GatewayInstance>> RegisterInstance([FromBody] GatewayInstanceRegisterRequest request)
    {
        var instance = await _gatewayInstanceService.RegisterInstanceAsync(request);
        return CreatedAtAction(nameof(GetInstance), new { id = instance.InstanceId }, instance);
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] GatewayInstanceHeartbeatRequest? request)
    {
        if (request == null || string.IsNullOrEmpty(request.InstanceId))
            return BadRequest(new { message = "InstanceId is required" });

        var success = await _gatewayInstanceService.UpdateHeartbeatAsync(request);
        if (!success)
            return NotFound(new { message = $"Instance {request.InstanceId} not found" });

        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteInstance(string id)
    {
        await _gatewayInstanceService.RemoveInstanceAsync(id);
        return NoContent();
    }

    /// <summary>
    /// 获取指定网关实例的当前 YARP 运行时配置
    /// </summary>
    [HttpGet("{id}/config")]
    public async Task<IActionResult> GetInstanceConfig(string id)
    {
        var result = await _configQueryService.QueryConfigAsync(id);

        if (!result.IsSuccess)
        {
            return result.FailureReason switch
            {
                ConfigQueryFailureReason.NotFound => NotFound(new { message = result.ErrorMessage }),
                ConfigQueryFailureReason.Offline => StatusCode(503, new { message = result.ErrorMessage }),
                ConfigQueryFailureReason.Timeout => StatusCode(504, new { message = result.ErrorMessage }),
                _ => StatusCode(500, new { message = result.ErrorMessage })
            };
        }

        // 解析 JSON 并返回
        try
        {
            var configDoc = JsonDocument.Parse(result.DataJson!);
            return Ok(configDoc.RootElement);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse config JSON for instance {InstanceId}", id);
            return StatusCode(500, new { message = "Invalid config data received from instance" });
        }
    }
}
