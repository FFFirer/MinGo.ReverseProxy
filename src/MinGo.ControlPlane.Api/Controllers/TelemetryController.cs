using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Services;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
    private readonly TelemetryStore _store;

    public TelemetryController(TelemetryStore store)
    {
        _store = store;
    }

    [HttpGet("traces")]
    public IActionResult GetTraces()
    {
        var traces = _store.Traces.ToList();
        return Ok(traces.Select(t => new
        {
            Name = t?.DisplayName,
            Duration = t?.GetTagValue("duration"),
            Status = t?.GetTagValue("status"),
            Route = t?.GetTagValue("route"),
            StartTime = t?.StartTimeUtc
        }));
    }

    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        return Ok(_store.Metrics);
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var totalRequests = _store.Metrics.TryGetValue("gateway.requests.total", out var requestPoints)
            ? requestPoints.Count : 0;
        var errorRequests = _store.Metrics.TryGetValue("gateway.requests.errors", out var errorPoints)
            ? errorPoints.Count : 0;
        var durationPoints = _store.Metrics.TryGetValue("gateway.request.duration", out var durationPointsList)
            ? durationPointsList : new List<MetricPoint>();
        var avgDuration = durationPoints.Count > 0 ? durationPoints.Average(p => p.Value) : 0;

        return Ok(new
        {
            TotalRequests = totalRequests,
            ErrorRequests = errorRequests,
            ErrorRate = totalRequests > 0 ? (double)errorRequests / totalRequests * 100 : 0,
            AverageDuration = avgDuration
        });
    }
}

public static class ActivityExtensions
{
    public static object? GetTagValue(this System.Diagnostics.Activity activity, string key)
    {
        return activity?.Tags.FirstOrDefault(t => t.Key == key).Value;
    }
}
