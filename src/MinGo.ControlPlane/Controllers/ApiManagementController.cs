using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Services;
using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoutesController : ControllerBase
{
    private readonly IApiManagementService _apiManagementService;
    private readonly ILogger<RoutesController> _logger;

    public RoutesController(IApiManagementService apiManagementService, ILogger<RoutesController> logger)
    {
        _apiManagementService = apiManagementService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MinGo.Shared.Models.RouteConfig>>> GetRoutes()
    {
        var routes = await _apiManagementService.GetRoutesAsync();
        return Ok(routes);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<MinGo.Shared.Models.RouteConfig>> GetRoute(string id)
    {
        var route = await _apiManagementService.GetRouteAsync(id);
        if (route == null || string.IsNullOrEmpty(route.ClusterId))
        {
            return NotFound();
        }
        return Ok(route);
    }

    [HttpPost]
    public async Task<ActionResult<MinGo.Shared.Models.RouteConfig>> CreateRoute(
        [FromBody] MinGo.Shared.Models.RouteConfig route)
    {
        var created = await _apiManagementService.CreateRouteAsync(route);
        return CreatedAtAction(nameof(GetRoute), new { id = created.ClusterId }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<MinGo.Shared.Models.RouteConfig>> UpdateRoute(
        string id,
        [FromBody] MinGo.Shared.Models.RouteConfig route)
    {
        var updated = await _apiManagementService.UpdateRouteAsync(id, route);
        if (updated == null || string.IsNullOrEmpty(updated.ClusterId))
        {
            return NotFound();
        }
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRoute(string id)
    {
        await _apiManagementService.DeleteRouteAsync(id);
        return NoContent();
    }
}

[ApiController]
[Route("api/[controller]")]
public class ClustersController : ControllerBase
{
    private readonly IApiManagementService _apiManagementService;
    private readonly ILogger<ClustersController> _logger;

    public ClustersController(IApiManagementService apiManagementService, ILogger<ClustersController> logger)
    {
        _apiManagementService = apiManagementService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClusterConfig>>> GetClusters()
    {
        var clusters = await _apiManagementService.GetClustersAsync();
        return Ok(clusters);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ClusterConfig>> GetCluster(string id)
    {
        var cluster = await _apiManagementService.GetClusterAsync(id);
        if (cluster == null || string.IsNullOrEmpty(cluster.LoadBalancingPolicy))
        {
            return NotFound();
        }
        return Ok(cluster);
    }

    [HttpPost]
    public async Task<ActionResult<ClusterConfig>> CreateCluster([FromBody] ClusterConfig cluster)
    {
        var created = await _apiManagementService.CreateClusterAsync(cluster);
        return CreatedAtAction(nameof(GetCluster), new { id = created.LoadBalancingPolicy }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ClusterConfig>> UpdateCluster(
        string id,
        [FromBody] ClusterConfig cluster)
    {
        var updated = await _apiManagementService.UpdateClusterAsync(id, cluster);
        if (updated == null || string.IsNullOrEmpty(updated.LoadBalancingPolicy))
        {
            return NotFound();
        }
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCluster(string id)
    {
        await _apiManagementService.DeleteClusterAsync(id);
        return NoContent();
    }

    [HttpPost("{clusterId}/destinations")]
    public async Task<ActionResult<ClusterConfig>> AddDestination(
        string clusterId,
        [FromQuery] string destinationId,
        [FromBody] DestinationConfig destination)
    {
        var cluster = await _apiManagementService.AddDestinationAsync(clusterId, destinationId, destination);
        if (cluster == null || string.IsNullOrEmpty(cluster.Id))
        {
            return NotFound();
        }
        return Ok(cluster);
    }

    [HttpPut("{clusterId}/destinations/{destinationId}")]
    public async Task<ActionResult<ClusterConfig>> UpdateDestination(
        string clusterId,
        string destinationId,
        [FromBody] DestinationConfig destination)
    {
        var cluster = await _apiManagementService.UpdateDestinationAsync(clusterId, destinationId, destination);
        if (cluster == null || string.IsNullOrEmpty(cluster.Id))
        {
            return NotFound();
        }
        return Ok(cluster);
    }

    [HttpDelete("{clusterId}/destinations/{destinationId}")]
    public async Task<ActionResult<ClusterConfig>> DeleteDestination(
        string clusterId,
        string destinationId)
    {
        var cluster = await _apiManagementService.RemoveDestinationAsync(clusterId, destinationId);
        if (cluster == null || string.IsNullOrEmpty(cluster.Id))
        {
            return NotFound();
        }
        return Ok(cluster);
    }
}

[ApiController]
[Route("api/[controller]")]
public class ApiManagementController : ControllerBase
{
    private readonly IApiManagementService _apiManagementService;
    private readonly ILogger<ApiManagementController> _logger;

    public ApiManagementController(IApiManagementService apiManagementService, ILogger<ApiManagementController> logger)
    {
        _apiManagementService = apiManagementService;
        _logger = logger;
    }

    /// <summary>
    /// 获取当前最新的集群配置项及路由配置
    /// </summary>
    /// <returns>包含集群和路由配置的响应</returns>
    [HttpGet("config")]
    public async Task<ActionResult<object>> GetCurrentConfig()
    {
        var routes = await _apiManagementService.GetRoutesAsync();
        var clusters = await _apiManagementService.GetClustersAsync();

        var config = new
        {
            Routes = routes,
            Clusters = clusters
        };

        return Ok(config);
    }
}
