using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ApiManagementController : ControllerBase
{
    private readonly IApiManagementService _apiManagementService;
    private readonly CloudflareSyncService _cloudflareSyncService;
    private readonly ILogger<ApiManagementController> _logger;

    public ApiManagementController(
        IApiManagementService apiManagementService,
        CloudflareSyncService cloudflareSyncService,
        ILogger<ApiManagementController> logger)
    {
        _apiManagementService = apiManagementService;
        _cloudflareSyncService = cloudflareSyncService;
        _logger = logger;
    }

    [HttpGet("routes")]
    public async Task<ActionResult<IEnumerable<RouteConfig>>> GetRoutes()
    {
        var routes = await _apiManagementService.GetRoutesAsync();
        return Ok(routes);
    }

    [HttpGet("routes/{id}")]
    public async Task<ActionResult<RouteConfig>> GetRoute(string id)
    {
        var route = await _apiManagementService.GetRouteAsync(id);
        if (route == null) return NotFound();
        return Ok(route);
    }

    [HttpPost("routes")]
    public async Task<ActionResult<RouteSaveResponse>> CreateRoute([FromBody] RouteConfig route, CancellationToken ct)
    {
        var validation = await _cloudflareSyncService.ValidateRouteAsync(route, null, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new { message = validation.Error });
        }

        var uniqueness = RouteUniquenessPolicy.Check(route, await _apiManagementService.GetRoutesAsync());
        if (!uniqueness.IsValid)
        {
            return BadRequest(new { message = uniqueness.Error });
        }

        var created = await _apiManagementService.CreateRouteAsync(route);
        var publish = await PublishAsync(created, null, ct);
        return CreatedAtAction(nameof(GetRoute), new { id = created.Id },
            new RouteSaveResponse(created, publish));
    }

    [HttpPut("routes/{id}")]
    public async Task<ActionResult<RouteSaveResponse>> UpdateRoute(string id, [FromBody] RouteConfig route, CancellationToken ct)
    {
        var validation = await _cloudflareSyncService.ValidateRouteAsync(route, id, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new { message = validation.Error });
        }

        var previous = await _apiManagementService.GetRouteAsync(id);
        if (previous == null) return NotFound();

        // 去重口径要在写库前判：启用一条路由（包括将停用改为启用）同样可能撞上别人的域名+路径
        var uniqueness = RouteUniquenessPolicy.Check(route, await _apiManagementService.GetRoutesAsync(), id);
        if (!uniqueness.IsValid)
        {
            return BadRequest(new { message = uniqueness.Error });
        }

        var updated = await _apiManagementService.UpdateRouteAsync(id, route);
        if (updated == null) return NotFound();

        var publish = await PublishAsync(updated, previous.ConnectionId, ct);
        return Ok(new RouteSaveResponse(updated, publish));
    }

    [HttpDelete("routes/{id}")]
    public async Task<IActionResult> DeleteRoute(string id, CancellationToken ct)
    {
        var existing = await _apiManagementService.GetRouteAsync(id);
        await _apiManagementService.DeleteRouteAsync(id);
        if (existing is not null)
        {
            await _cloudflareSyncService.SyncRouteDeletedAsync(existing, ct);
        }
        return NoContent();
    }

    private async Task<RoutePublishInfo?> PublishAsync(
        RouteConfig route, string? previousConnectionId, CancellationToken ct)
    {
        if (ConnectionIds.IsLocal(route.ConnectionId) && ConnectionIds.IsLocal(previousConnectionId))
        {
            return null;
        }

        // 新旧通道都可能需要重对账（切换通道时旧隧道要摘除发布）
        var target = ConnectionIds.IsLocal(route.ConnectionId) ? previousConnectionId : route.ConnectionId;
        var results = new List<ReconcileReport>();

        try
        {
            var tunnels = new[] { previousConnectionId, route.ConnectionId }
                .Where(id => !ConnectionIds.IsLocal(id))
                .Distinct(StringComparer.Ordinal);

            foreach (var tunnelId in tunnels)
            {
                try
                {
                    results.Add(await _cloudflareSyncService.ReconcileTunnelAsync(tunnelId!, ct));
                }
                catch (CloudflareException ex)
                {
                    results.Add(new ReconcileReport(
                        tunnelId!, false, [], [], [], [ex.Message]));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "路由 [{RouteId}] 发布同步失败", route.Id);
            return new RoutePublishInfo(target ?? ConnectionIds.Local, false, ex.Message, [], [], []);
        }

        return new RoutePublishInfo(
            target ?? ConnectionIds.Local,
            true,
            null,
            results.SelectMany(r => r.DnsCreated).ToArray(),
            results.SelectMany(r => r.DnsDeleted).ToArray(),
            results.SelectMany(r => r.Conflicts).ToArray());
    }

    [HttpGet("clusters")]
    public async Task<ActionResult<IEnumerable<ClusterConfig>>> GetClusters()
    {
        var clusters = await _apiManagementService.GetClustersAsync();
        return Ok(clusters);
    }

    [HttpGet("clusters/{id}")]
    public async Task<ActionResult<ClusterConfig>> GetCluster(string id)
    {
        var cluster = await _apiManagementService.GetClusterAsync(id);
        if (cluster == null) return NotFound();
        return Ok(cluster);
    }

    [HttpPost("clusters")]
    public async Task<ActionResult<ClusterConfig>> CreateCluster([FromBody] ClusterConfig cluster)
    {
        if (string.IsNullOrWhiteSpace(cluster.Id))
        {
            return BadRequest(new { message = "集群名称不能为空" });
        }

        if (await _apiManagementService.GetClusterAsync(cluster.Id) is not null)
        {
            return Conflict(new { message = $"集群 '{cluster.Id}' 已存在" });
        }

        var created = await _apiManagementService.CreateClusterAsync(cluster);
        return CreatedAtAction(nameof(GetCluster), new { id = created.Id }, created);
    }

    [HttpPut("clusters/{id}")]
    public async Task<ActionResult<ClusterConfig>> UpdateCluster(string id, [FromBody] ClusterConfig cluster)
    {
        var updated = await _apiManagementService.UpdateClusterAsync(id, cluster);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    [HttpDelete("clusters/{id}")]
    public async Task<IActionResult> DeleteCluster(string id)
    {
        await _apiManagementService.DeleteClusterAsync(id);
        return NoContent();
    }
}

/// <summary>路由保存响应：路由本体 + 隧道发布结果（local 通道时为 null）。</summary>
public sealed record RouteSaveResponse(RouteConfig Route, RoutePublishInfo? Publish);

/// <summary>隧道发布结果：DnsCreated/DnsDeleted 为本次变更，Conflicts 为需要人工处理的冲突。</summary>
public sealed record RoutePublishInfo(
    string ConnectionId,
    bool Published,
    string? Error,
    IReadOnlyList<string> DnsCreated,
    IReadOnlyList<string> DnsDeleted,
    IReadOnlyList<string> Conflicts);
