using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.Models.Portainer;
using MinGo.ControlPlane.Api.Services;
using MinGo.Core.Interfaces;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/portainer")]
[Authorize]
public class PortainerController : ControllerBase
{
    private readonly PortainerDiscoveryService _discoveryService;
    private readonly IPortainerInstanceService _instanceService;
    private readonly PortainerClientFactory _clientFactory;
    private readonly ILogger<PortainerController> _logger;

    public PortainerController(
        PortainerDiscoveryService discoveryService,
        IPortainerInstanceService instanceService,
        PortainerClientFactory clientFactory,
        ILogger<PortainerController> logger)
    {
        _discoveryService = discoveryService;
        _instanceService = instanceService;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    // ── 实例 CRUD ──

    [HttpGet("instances")]
    public async Task<ActionResult<IEnumerable<PortainerInstanceDto>>> GetInstances()
    {
        return Ok(await _instanceService.GetAllAsync());
    }

    [HttpGet("instances/{id}")]
    public async Task<ActionResult<PortainerInstanceDto>> GetInstance(string id)
    {
        var instance = await _instanceService.GetByIdAsync(id);
        if (instance is null) return NotFound();
        return Ok(instance);
    }

    [HttpPost("instances")]
    public async Task<ActionResult<PortainerInstanceDto>> CreateInstance([FromBody] PortainerInstanceDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            return BadRequest(new { message = "实例 ID 不能为空" });
        }

        if (await _instanceService.GetByIdAsync(dto.Id) is not null)
        {
            return Conflict(new { message = $"Portainer 实例 '{dto.Id}' 已存在" });
        }

        var created = await _instanceService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetInstance), new { id = created.Id }, created);
    }

    [HttpPut("instances/{id}")]
    public async Task<ActionResult<PortainerInstanceDto>> UpdateInstance(string id, [FromBody] PortainerInstanceDto dto)
    {
        var updated = await _instanceService.UpdateAsync(id, dto);
        if (updated is null) return NotFound();

        // 配置变更后清除缓存的客户端
        _clientFactory.Invalidate(id);
        return Ok(updated);
    }

    [HttpDelete("instances/{id}")]
    public async Task<IActionResult> DeleteInstance(string id)
    {
        await _instanceService.DeleteAsync(id);
        _clientFactory.Invalidate(id);
        return NoContent();
    }

    // ── 发现/状态 ──

    [HttpPost("instances/{id}/test")]
    public async Task<ActionResult<PortainerStatusResponse>> TestConnection(string id, CancellationToken ct)
    {
        try
        {
            return Ok(await _discoveryService.GetStatusAsync(id, ct));
        }
        catch (PortainerException ex)
        {
            _logger.LogWarning(ex, "Portainer [{InstanceId}] 连接测试失败", id);
            return Ok(new PortainerStatusResponse(
                id, true, null, false, null, null, true,
                PortainerSelfIdentity.Empty, ex.Message));
        }
    }

    [HttpGet("{id}/status")]
    public async Task<ActionResult<PortainerStatusResponse>> GetStatus(string id, CancellationToken ct)
    {
        try
        {
            return Ok(await _discoveryService.GetStatusAsync(id, ct));
        }
        catch (PortainerException ex)
        {
            _logger.LogWarning(ex, "Portainer [{InstanceId}] 状态检查失败", id);
            return Ok(new PortainerStatusResponse(
                id, true, null, false, null, null, true,
                PortainerSelfIdentity.Empty, ex.Message));
        }
    }

    [HttpGet("{id}/discovery")]
    public async Task<ActionResult<PortainerDiscoveryResponse>> GetDiscovery(
        string id,
        [FromQuery] int? endpointId,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _discoveryService.GetDiscoveryAsync(id, endpointId, ct));
        }
        catch (PortainerException ex)
        {
            _logger.LogWarning(ex, "Portainer [{InstanceId}] 发现失败", id);

            var statusCode = ex.StatusCode is >= 400 and < 500 and not 401 and not 403
                ? ex.StatusCode
                : 503;

            return Problem(
                statusCode: statusCode,
                title: statusCode == 503 ? "Portainer 不可用" : "Portainer 请求失败",
                detail: ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }
}
