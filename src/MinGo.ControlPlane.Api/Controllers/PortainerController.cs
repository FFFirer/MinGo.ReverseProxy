using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.Models.Portainer;
using MinGo.ControlPlane.Api.Services;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/portainer")]
[Authorize]
public class PortainerController : ControllerBase
{
    private readonly PortainerDiscoveryService _discoveryService;
    private readonly ILogger<PortainerController> _logger;

    public PortainerController(
        PortainerDiscoveryService discoveryService,
        ILogger<PortainerController> logger)
    {
        _discoveryService = discoveryService;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<ActionResult<PortainerStatusResponse>> GetStatus(CancellationToken ct)
    {
        return Ok(await _discoveryService.GetStatusAsync(ct));
    }

    [HttpGet("discovery")]
    public async Task<ActionResult<PortainerDiscoveryResponse>> GetDiscovery(
        [FromQuery] int? endpointId,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _discoveryService.GetDiscoveryAsync(endpointId, ct));
        }
        catch (PortainerException ex)
        {
            _logger.LogWarning(ex, "Portainer 发现失败");

            // 4xx（如 endpoint 不存在）原样返回；认证失败必须转 503，
            // 避免前端把 Portainer 的 401 当成控制台会话失效而登出。
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
