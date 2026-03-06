using Microsoft.AspNetCore.Mvc;
using MinGo.Application.Services;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;

namespace MinGo.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class InstancesController : ControllerBase
{
    private readonly IGatewayInstanceService _gatewayInstanceService;

    public InstancesController(IGatewayInstanceService gatewayInstanceService)
    {
        _gatewayInstanceService = gatewayInstanceService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<GatewayInstance>> RegisterInstance(GatewayInstanceRegisterRequest request)
    {
        var instance = await _gatewayInstanceService.RegisterInstanceAsync(request);
        return Ok(instance);
    }

    [HttpPost("heartbeat")]
    public async Task<ActionResult<bool>> UpdateHeartbeat(GatewayInstanceHeartbeatRequest request)
    {
        var success = await _gatewayInstanceService.UpdateHeartbeatAsync(request);
        return Ok(success);
    }

    [HttpGet]
    public async Task<ActionResult<GatewayInstanceListResponse>> GetInstances()
    {
        var instances = await _gatewayInstanceService.GetInstancesAsync();
        return Ok(instances);
    }

    [HttpGet("{instanceId}")]
    public async Task<ActionResult<GatewayInstance>> GetInstance(string instanceId)
    {
        var instance = await _gatewayInstanceService.GetInstanceAsync(instanceId);
        if (instance == null)
        {
            return NotFound();
        }
        return Ok(instance);
    }

    [HttpDelete("{instanceId}")]
    public async Task<ActionResult<bool>> RemoveInstance(string instanceId)
    {
        var success = await _gatewayInstanceService.RemoveInstanceAsync(instanceId);
        return Ok(success);
    }

    [HttpPost("check-timeout")]
    public async Task<ActionResult<int>> CheckTimeoutInstances()
    {
        var count = await _gatewayInstanceService.CheckAndUpdateTimeoutInstancesAsync();
        return Ok(count);
    }

    [HttpPost("cleanup-timeout")]
    public async Task<ActionResult<int>> CleanupTimeoutInstances()
    {
        var count = await _gatewayInstanceService.CleanupLongTimeTimeoutInstancesAsync();
        return Ok(count);
    }
}