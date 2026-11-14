using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Services;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
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
            Duration = t.GetTagValue("duration"),
            Status = t.GetTagValue("status"),
            Route = t.GetTagValue("route"),
            StartTime = t?.StartTimeUtc
        }));
    }

    [HttpGet("metrics")]
    public IActionResult GetMetrics([FromQuery] string? instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            return Ok(_store.Metrics);
        }

        // ingest 给每个点打上 instance_id；此前这个标签只写不读，多实例数据在看板上被无区分地混算
        var filtered = _store.Metrics
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value
                    .Where(p => MatchesInstance(p, instanceId))
                    .ToList());

        return Ok(filtered);
    }

    [HttpGet("stats")]
    public IActionResult GetStats([FromQuery] string? instanceId)
    {
        // 口径与 MonitoringService 一致：对点值求和，而不是数点的个数。
        // 每请求点值为 1 时两者暂时相等，但上报量修好之后，Count 与 Sum 的差就是抽样率的差。
        var totalRequests = PointsFor(GatewayMetricNames.RequestsTotal, instanceId).Sum(p => p.Value);
        var errorRequests = PointsFor(GatewayMetricNames.RequestsErrors, instanceId).Sum(p => p.Value);
        var durations = PointsFor(GatewayMetricNames.RequestDuration, instanceId).Select(p => p.Value).ToList();

        return Ok(new
        {
            TotalRequests = totalRequests,
            ErrorRequests = errorRequests,
            ErrorRate = totalRequests > 0 ? errorRequests / totalRequests * 100 : 0,
            AverageDuration = durations.Count > 0 ? durations.Average() : 0.0
        });
    }

    private static bool MatchesInstance(MetricPoint point, string instanceId) =>
        point.Tags.TryGetValue("instance_id", out var tag)
        && string.Equals(tag?.ToString(), instanceId, StringComparison.Ordinal);

    /// <summary>
    /// 取某指标在控制面保存的原始点。
    /// 控制面不做增量抽取（游标是数据面心跳的事），这里只按 instance_id 过滤。
    /// </summary>
    private List<MetricPoint> PointsFor(string name, string? instanceId)
    {
        if (!_store.Metrics.TryGetValue(name, out var queue))
        {
            return new List<MetricPoint>();
        }

        var points = queue.ToList();
        if (string.IsNullOrEmpty(instanceId))
        {
            return points;
        }

        return points
            .Where(p => p.Tags.TryGetValue("instance_id", out var tag)
                        && string.Equals(tag?.ToString(), instanceId, StringComparison.Ordinal))
            .ToList();
    }
}

public static class ActivityExtensions
{
    /// <summary>
    /// 读取标签值。
    /// 不能用 Activity.Tags：.NET 10 下该投影只暴露字符串形态的标签值，
    /// SetTag("status", 500) 与 SetTag("duration", 12.3) 在那里读出来是 null。
    /// </summary>
    public static object? GetTagValue(this System.Diagnostics.Activity? activity, string key)
    {
        if (activity is null)
        {
            return null;
        }

        foreach (var tag in activity.EnumerateTagObjects())
        {
            if (string.Equals(tag.Key, key, StringComparison.Ordinal))
            {
                return tag.Value;
            }
        }

        return null;
    }
}
