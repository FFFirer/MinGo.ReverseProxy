using Microsoft.AspNetCore.Mvc;
using MinGo.Application.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
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
        if (route == null)
        {
            return NotFound();
        }
        return Ok(route);
    }

    [HttpPost("routes")]
    public async Task<ActionResult<RouteConfig>> CreateRoute(RouteConfig route)
    {
        var createdRoute = await _apiManagementService.CreateRouteAsync(route);
        return CreatedAtAction(nameof(GetRoute), new { id = createdRoute.Id }, createdRoute);
    }

    [HttpPut("routes/{id}")]
    public async Task<ActionResult<RouteConfig>> UpdateRoute(string id, RouteConfig route)
    {
        var updatedRoute = await _apiManagementService.UpdateRouteAsync(id, route);
        if (updatedRoute == null)
        {
            return NotFound();
        }
        return Ok(updatedRoute);
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
        if (cluster == null)
        {
            return NotFound();
        }
        return Ok(cluster);
    }

    [HttpPost("clusters")]
    public async Task<ActionResult<ClusterConfig>> CreateCluster(ClusterConfig cluster)
    {
        var createdCluster = await _apiManagementService.CreateClusterAsync(cluster);
        return CreatedAtAction(nameof(GetCluster), new { id = createdCluster.Id }, createdCluster);
    }

    [HttpPut("clusters/{id}")]
    public async Task<ActionResult<ClusterConfig>> UpdateCluster(string id, ClusterConfig cluster)
    {
        var updatedCluster = await _apiManagementService.UpdateClusterAsync(id, cluster);
        if (updatedCluster == null)
        {
            return NotFound();
        }
        return Ok(updatedCluster);
    }

    [HttpDelete("clusters/{id}")]
    public async Task<IActionResult> DeleteCluster(string id)
    {
        await _apiManagementService.DeleteClusterAsync(id);
        return NoContent();
    }

    [HttpPost("clusters/{clusterId}/destinations/{destinationId}")]
    public async Task<ActionResult<ClusterConfig>> AddDestination(string clusterId, string destinationId, DestinationConfig destination)
    {
        var cluster = await _apiManagementService.AddDestinationAsync(clusterId, destinationId, destination);
        if (cluster == null)
        {
            return NotFound();
        }
        return Ok(cluster);
    }

    [HttpPut("clusters/{clusterId}/destinations/{destinationId}")]
    public async Task<ActionResult<ClusterConfig>> UpdateDestination(string clusterId, string destinationId, DestinationConfig destination)
    {
        var cluster = await _apiManagementService.UpdateDestinationAsync(clusterId, destinationId, destination);
        if (cluster == null)
        {
            return NotFound();
        }
        return Ok(cluster);
    }

    [HttpDelete("clusters/{clusterId}/destinations/{destinationId}")]
    public async Task<ActionResult<ClusterConfig>> RemoveDestination(string clusterId, string destinationId)
    {
        var cluster = await _apiManagementService.RemoveDestinationAsync(clusterId, destinationId);
        if (cluster == null)
        {
            return NotFound();
        }
        return Ok(cluster);
    }

    [HttpGet("config")]
    public async Task<ActionResult<object>> GetConfig()
    {
        var routes = await _apiManagementService.GetRoutesAsync();
        var clusters = await _apiManagementService.GetClustersAsync();

        return Ok(new
        {
            Routes = routes,
            Clusters = clusters
        });
    }

    [HttpGet("certificates")]
    public async Task<ActionResult<IEnumerable<CertificateConfig>>> GetCertificates()
    {
        var certificates = await _apiManagementService.GetCertificatesAsync();
        return Ok(certificates);
    }

    [HttpGet("certificates/{id}")]
    public async Task<ActionResult<CertificateConfig>> GetCertificate(string id)
    {
        var certificate = await _apiManagementService.GetCertificateAsync(id);
        if (certificate == null)
        {
            return NotFound();
        }
        return Ok(certificate);
    }

    [HttpPost("certificates")]
    public async Task<ActionResult<CertificateConfig>> CreateCertificate(CertificateConfig certificate)
    {
        var createdCertificate = await _apiManagementService.CreateCertificateAsync(certificate);
        return CreatedAtAction(nameof(GetCertificate), new { id = createdCertificate.Id }, createdCertificate);
    }

    [HttpPut("certificates/{id}")]
    public async Task<ActionResult<CertificateConfig>> UpdateCertificate(string id, CertificateConfig certificate)
    {
        var updatedCertificate = await _apiManagementService.UpdateCertificateAsync(id, certificate);
        if (updatedCertificate == null)
        {
            return NotFound();
        }
        return Ok(updatedCertificate);
    }

    [HttpDelete("certificates/{id}")]
    public async Task<IActionResult> DeleteCertificate(string id)
    {
        await _apiManagementService.DeleteCertificateAsync(id);
        return NoContent();
    }
}