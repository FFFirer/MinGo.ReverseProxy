using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.Services;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/cloudflare")]
[Authorize]
public class CloudflareController : ControllerBase
{
    private readonly CloudflareClient _client;
    private readonly CloudflareSyncService _syncService;
    private readonly ILogger<CloudflareController> _logger;

    public CloudflareController(
        CloudflareClient client,
        CloudflareSyncService syncService,
        ILogger<CloudflareController> logger)
    {
        _client = client;
        _syncService = syncService;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<ActionResult<CloudflareStatusInfo>> GetStatus(CancellationToken ct)
    {
        return Ok(await _syncService.GetStatusAsync(ct));
    }

    [HttpGet("zones")]
    public async Task<ActionResult<IEnumerable<CloudflareZone>>> GetZones(CancellationToken ct)
    {
        try
        {
            return Ok(await _client.ListZonesAsync(ct));
        }
        catch (CloudflareException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    // ── 隧道 CRUD ──

    [HttpGet("tunnels")]
    public async Task<ActionResult<IEnumerable<CloudflareTunnel>>> GetTunnels(CancellationToken ct)
    {
        try
        {
            return Ok(await _client.ListTunnelsAsync(ct));
        }
        catch (CloudflareException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    [HttpPost("tunnels")]
    public async Task<ActionResult<CloudflareTunnel>> CreateTunnel([FromBody] CreateTunnelRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "隧道名称不能为空" });
        }

        try
        {
            var created = await _client.CreateTunnelAsync(request.Name.Trim(), ct);
            return CreatedAtAction(nameof(GetTunnels), null, created);
        }
        catch (CloudflareException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    [HttpPatch("tunnels/{tunnelId}")]
    public async Task<ActionResult<CloudflareTunnel>> UpdateTunnel(
        string tunnelId, [FromBody] CreateTunnelRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "隧道名称不能为空" });
        }

        try
        {
            return Ok(await _client.UpdateTunnelAsync(tunnelId, request.Name.Trim(), ct));
        }
        catch (CloudflareException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    [HttpDelete("tunnels/{tunnelId}")]
    public async Task<IActionResult> DeleteTunnel(string tunnelId, CancellationToken ct)
    {
        var bound = await _syncService.GetRoutesBoundToAsync(tunnelId);
        if (bound.Count > 0)
        {
            return Conflict(new
            {
                message = $"隧道仍被 {bound.Count} 条路由绑定，请先将这些路由切换到本地数据面或其他隧道",
                routes = bound.Select(r => new { r.Id, r.Name, host = r.Match?.Host }).ToArray(),
            });
        }

        try
        {
            await _client.DeleteTunnelAsync(tunnelId, ct);
            return NoContent();
        }
        catch (CloudflareException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    // ── 连接与 ingress ──

    [HttpGet("tunnels/{tunnelId}/connections")]
    public async Task<ActionResult<IEnumerable<TunnelConnection>>> GetConnections(string tunnelId, CancellationToken ct)
    {
        try
        {
            return Ok(await _client.ListConnectionsAsync(tunnelId, ct));
        }
        catch (CloudflareException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    [HttpGet("tunnels/{tunnelId}/ingress")]
    public async Task<ActionResult<TunnelIngressView>> GetIngress(string tunnelId, CancellationToken ct)
    {
        try
        {
            return Ok(await _syncService.GetIngressViewAsync(tunnelId, ct));
        }
        catch (CloudflareException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    [HttpPost("tunnels/{tunnelId}/reconcile")]
    public async Task<ActionResult<ReconcileReport>> Reconcile(string tunnelId, CancellationToken ct)
    {
        try
        {
            return Ok(await _syncService.ReconcileTunnelAsync(tunnelId, ct));
        }
        catch (CloudflareException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    private ObjectResult Unavailable(string message)
    {
        _logger.LogWarning("Cloudflare 请求失败: {Message}", message);
        return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Cloudflare 不可用", detail: message);
    }
}

public sealed record CreateTunnelRequest(string Name);
