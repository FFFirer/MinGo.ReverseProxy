using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApiManagementController : ControllerBase
{
    private readonly IApiManagementService _apiManagementService;

    public ApiManagementController(IApiManagementService apiManagementService)
    {
        _apiManagementService = apiManagementService;
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
    public async Task<ActionResult<RouteConfig>> CreateRoute([FromBody] RouteConfig route)
    {
        var created = await _apiManagementService.CreateRouteAsync(route);
        return CreatedAtAction(nameof(GetRoute), new { id = created.Id }, created);
    }

    [HttpPut("routes/{id}")]
    public async Task<ActionResult<RouteConfig>> UpdateRoute(string id, [FromBody] RouteConfig route)
    {
        var updated = await _apiManagementService.UpdateRouteAsync(id, route);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    [HttpDelete("routes/{id}")]
    public async Task<IActionResult> DeleteRoute(string id)
    {
        await _apiManagementService.DeleteRouteAsync(id);
        return NoContent();
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
