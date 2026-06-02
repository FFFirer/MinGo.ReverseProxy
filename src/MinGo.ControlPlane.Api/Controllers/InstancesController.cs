using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Entities;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InstancesController : ControllerBase
{
    private readonly IGatewayInstanceService _gatewayInstanceService;

    public InstancesController(IGatewayInstanceService gatewayInstanceService)
    {
        _gatewayInstanceService = gatewayInstanceService;
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
}
