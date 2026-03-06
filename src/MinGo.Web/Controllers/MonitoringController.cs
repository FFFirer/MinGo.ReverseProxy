using Microsoft.AspNetCore.Mvc;
using MinGo.Application.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class MonitoringController : ControllerBase
{
    private readonly IMonitoringService _monitoringService;

    public MonitoringController(IMonitoringService monitoringService)
    {
        _monitoringService = monitoringService;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<MetricsSummary>> GetMetricsSummary()
    {
        var summary = await _monitoringService.GetMetricsSummaryAsync();
        return Ok(summary);
    }

    [HttpGet("requests")]
    public async Task<ActionResult<IEnumerable<RequestMetrics>>> GetRequestMetrics([FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var metrics = await _monitoringService.GetRequestMetricsAsync(start, end);
        return Ok(metrics);
    }

    [HttpGet("services")]
    public async Task<ActionResult<IEnumerable<ServiceMetrics>>> GetServiceMetrics()
    {
        var metrics = await _monitoringService.GetServiceMetricsAsync();
        return Ok(metrics);
    }

    [HttpGet("errors")]
    public async Task<ActionResult<IEnumerable<ErrorMetrics>>> GetErrorMetrics([FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var metrics = await _monitoringService.GetErrorMetricsAsync(start, end);
        return Ok(metrics);
    }

    [HttpGet("system")]
    public async Task<ActionResult<SystemMetrics>> GetSystemMetrics()
    {
        var metrics = await _monitoringService.GetSystemMetricsAsync();
        return Ok(metrics);
    }
}