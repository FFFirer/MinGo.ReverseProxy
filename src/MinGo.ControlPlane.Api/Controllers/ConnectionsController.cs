using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.Services;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/connections")]
[Authorize]
public class ConnectionsController : ControllerBase
{
    private readonly CloudflareSyncService _syncService;

    public ConnectionsController(CloudflareSyncService syncService)
    {
        _syncService = syncService;
    }

    /// <summary>
    /// 路由表单的接入通道下拉数据源：内置本地数据面 + 各 Cloudflare 隧道。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConnectionSummary>>> GetConnections(CancellationToken ct)
    {
        return Ok(await _syncService.ListConnectionsAsync(ct));
    }
}
