using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using MinGo.Core.Services;

namespace MinGo.Application.Services;

/// <summary>
/// 监控服务实现
/// </summary>
public class MonitoringService : IMonitoringService
{
    private readonly TelemetryStore _store;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="store">遥测存储</param>
    public MonitoringService(TelemetryStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task<MetricsSummary> GetMetricsSummaryAsync()
    {
        var totalPoints = _store.Metrics.TryGetValue("gateway.requests.total", out var total) ? total : new List<MetricPoint>();
        var errorPoints = _store.Metrics.TryGetValue("gateway.requests.errors", out var errors) ? errors : new List<MetricPoint>();
        var durationPoints = _store.Metrics.TryGetValue("gateway.request.duration", out var durations) ? durations : new List<MetricPoint>();

        var totalRequests = totalPoints.Sum(p => (int)p.Value);
        var errorRequests = errorPoints.Sum(p => (int)p.Value);
        var avgResponseTime = durationPoints.Count > 0 ? durationPoints.Average(p => p.Value) : 0;

        var summary = new MetricsSummary
        {
            TotalRequests = totalRequests,
            ErrorRequests = errorRequests,
            ErrorRate = totalRequests > 0 ? (double)errorRequests / totalRequests * 100 : 0,
            AverageResponseTime = avgResponseTime,
            ActiveConnections = 0,
            CpuUsage = 0,
            MemoryUsage = 0,
            LastUpdated = DateTimeOffset.UtcNow
        };

        return Task.FromResult(summary);
    }

    /// <inheritdoc />
    public Task<IEnumerable<RequestMetrics>> GetRequestMetricsAsync(DateTimeOffset start, DateTimeOffset end)
    {
        var durationPoints = _store.Metrics.TryGetValue("gateway.request.duration", out var durations)
            ? durations.Where(p => p.Timestamp >= start.UtcDateTime && p.Timestamp <= end.UtcDateTime).ToList()
            : new List<MetricPoint>();

        var totalPoints = _store.Metrics.TryGetValue("gateway.requests.total", out var total)
            ? total.Where(p => p.Timestamp >= start.UtcDateTime && p.Timestamp <= end.UtcDateTime).ToList()
            : new List<MetricPoint>();

        var errorPoints = _store.Metrics.TryGetValue("gateway.requests.errors", out var errors)
            ? errors.Where(p => p.Timestamp >= start.UtcDateTime && p.Timestamp <= end.UtcDateTime).ToList()
            : new List<MetricPoint>();

        var metrics = new List<RequestMetrics>();
        if (durationPoints.Count > 0)
        {
            metrics.Add(new RequestMetrics
            {
                Timestamp = end,
                Count = totalPoints.Sum(p => (int)p.Value),
                ErrorCount = errorPoints.Sum(p => (int)p.Value),
                AverageResponseTime = durationPoints.Average(p => p.Value),
                P95ResponseTime = GetPercentile(durationPoints.Select(p => p.Value).ToList(), 95),
                P99ResponseTime = GetPercentile(durationPoints.Select(p => p.Value).ToList(), 99)
            });
        }

        return Task.FromResult(metrics.AsEnumerable());
    }

    /// <inheritdoc />
    public Task<IEnumerable<ServiceMetrics>> GetServiceMetricsAsync()
    {
        var totalPoints = _store.Metrics.TryGetValue("gateway.requests.total", out var total) ? total : new List<MetricPoint>();
        var errorPoints = _store.Metrics.TryGetValue("gateway.requests.errors", out var errors) ? errors : new List<MetricPoint>();
        var durationPoints = _store.Metrics.TryGetValue("gateway.request.duration", out var durations) ? durations : new List<MetricPoint>();

        var serviceGroups = totalPoints
            .GroupBy(p => p.Tags.TryGetValue("route", out var route) ? route?.ToString() ?? "unknown" : "unknown")
            .Select(g =>
            {
                var routeName = g.Key;
                var serviceTotal = g.Sum(p => (int)p.Value);
                var serviceErrors = errorPoints.Where(p => p.Tags.TryGetValue("route", out var r) && r?.ToString() == routeName).Sum(p => (int)p.Value);
                var serviceDurations = durationPoints.Where(p => p.Tags.TryGetValue("route", out var r) && r?.ToString() == routeName).ToList();

                return new ServiceMetrics
                {
                    ServiceId = routeName,
                    ServiceName = routeName,
                    TotalRequests = serviceTotal,
                    ErrorRequests = serviceErrors,
                    ErrorRate = serviceTotal > 0 ? (double)serviceErrors / serviceTotal * 100 : 0,
                    AverageResponseTime = serviceDurations.Count > 0 ? serviceDurations.Average(p => p.Value) : 0,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            });

        return Task.FromResult(serviceGroups.AsEnumerable());
    }

    /// <inheritdoc />
    public Task<IEnumerable<ErrorMetrics>> GetErrorMetricsAsync(DateTimeOffset start, DateTimeOffset end)
    {
        var errorPoints = _store.Metrics.TryGetValue("gateway.requests.errors", out var errors)
            ? errors.Where(p => p.Timestamp >= start.UtcDateTime && p.Timestamp <= end.UtcDateTime).ToList()
            : new List<MetricPoint>();

        var errorMetrics = errorPoints
            .GroupBy(p => p.Tags.TryGetValue("status", out var status) ? status?.ToString() ?? "unknown" : "unknown")
            .Select(g => new ErrorMetrics
            {
                Timestamp = end,
                ErrorCode = g.Key,
                ErrorMessage = $"HTTP {(int.TryParse(g.Key, out var code) ? code : 500)}",
                Count = g.Sum(p => (int)p.Value),
                RouteId = g.First().Tags.TryGetValue("route", out var route) ? route?.ToString() : null,
                ClusterId = g.First().Tags.TryGetValue("cluster", out var cluster) ? cluster?.ToString() : null
            });

        return Task.FromResult(errorMetrics.AsEnumerable());
    }

    /// <inheritdoc />
    public Task<SystemMetrics> GetSystemMetricsAsync()
    {
        var metrics = new SystemMetrics
        {
            CpuUsage = 0,
            MemoryUsage = 0,
            DiskUsage = 0,
            NetworkIn = 0,
            NetworkOut = 0,
            ActiveProcesses = 0,
            LastUpdated = DateTimeOffset.UtcNow
        };

        return Task.FromResult(metrics);
    }

    /// <summary>
    /// 计算百分位数
    /// </summary>
    /// <param name="values">值列表</param>
    /// <param name="percentile">百分位</param>
    /// <returns>百分位值</returns>
    private static double GetPercentile(List<double> values, int percentile)
    {
        if (values.Count == 0) return 0;

        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling((percentile / 100.0) * sorted.Count) - 1;
        index = Math.Max(0, Math.Min(sorted.Count - 1, index));
        return sorted[index];
    }
}
