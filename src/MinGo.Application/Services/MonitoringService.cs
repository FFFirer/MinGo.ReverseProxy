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
    private readonly IApiDbService _apiDb;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="store">遥测存储</param>
    /// <param name="apiDb">路由配置查询（用于路由 ID → 显示名映射）</param>
    public MonitoringService(TelemetryStore store, IApiDbService apiDb)
    {
        _store = store;
        _apiDb = apiDb;
    }

    private async Task<Dictionary<string, string>> GetRouteNamesAsync()
    {
        try
        {
            var routes = await _apiDb.GetRoutesAsync();
            return routes.Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .GroupBy(r => r.Id)
                .ToDictionary(g => g.Key, g => g.First().Name);
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <inheritdoc />
    public Task<MetricsSummary> GetMetricsSummaryAsync()
    {
        var totalPoints = _store.Metrics.TryGetValue("gateway.requests.total", out var total) ? total.ToList() : new List<MetricPoint>();
        var errorPoints = _store.Metrics.TryGetValue("gateway.requests.errors", out var errors) ? errors.ToList() : new List<MetricPoint>();
        var durationPoints = _store.Metrics.TryGetValue("gateway.request.duration", out var durations) ? durations.ToList() : new List<MetricPoint>();

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
        
        // 按分钟分组计算数据点
        var timeSpan = end - start;
        var intervalMinutes = Math.Min((int)timeSpan.TotalMinutes, 60); // 最多60个数据点
        if (intervalMinutes < 1) intervalMinutes = 1;
        
        for (int i = 0; i <= intervalMinutes; i++)
        {
            var intervalStart = start.AddMinutes(i);
            var intervalEnd = intervalStart.AddMinutes(1);
            
            if (intervalStart > end) break;
            
            var intervalDurationPoints = durationPoints.Where(p => p.Timestamp >= intervalStart.UtcDateTime && p.Timestamp < intervalEnd.UtcDateTime).ToList();
            var intervalTotalPoints = totalPoints.Where(p => p.Timestamp >= intervalStart.UtcDateTime && p.Timestamp < intervalEnd.UtcDateTime).ToList();
            var intervalErrorPoints = errorPoints.Where(p => p.Timestamp >= intervalStart.UtcDateTime && p.Timestamp < intervalEnd.UtcDateTime).ToList();
            
            if (intervalTotalPoints.Count > 0 || intervalErrorPoints.Count > 0)
            {
                metrics.Add(new RequestMetrics
                {
                    Timestamp = intervalStart,
                    Count = intervalTotalPoints.Sum(p => (int)p.Value),
                    ErrorCount = intervalErrorPoints.Sum(p => (int)p.Value),
                    AverageResponseTime = intervalDurationPoints.Count > 0 ? intervalDurationPoints.Average(p => p.Value) : 0,
                    P95ResponseTime = GetPercentile(intervalDurationPoints.Select(p => p.Value).ToList(), 95),
                    P99ResponseTime = GetPercentile(intervalDurationPoints.Select(p => p.Value).ToList(), 99)
                });
            }
            else
            {
                // 添加空数据点以保持图表连续
                metrics.Add(new RequestMetrics
                {
                    Timestamp = intervalStart,
                    Count = 0,
                    ErrorCount = 0,
                    AverageResponseTime = 0,
                    P95ResponseTime = 0,
                    P99ResponseTime = 0
                });
            }
        }

        return Task.FromResult(metrics.AsEnumerable());
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ServiceMetrics>> GetServiceMetricsAsync()
    {
        var totalPoints = _store.Metrics.TryGetValue("gateway.requests.total", out var total) ? total.ToList() : new List<MetricPoint>();
        var errorPoints = _store.Metrics.TryGetValue("gateway.requests.errors", out var errors) ? errors.ToList() : new List<MetricPoint>();
        var durationPoints = _store.Metrics.TryGetValue("gateway.request.duration", out var durations) ? durations.ToList() : new List<MetricPoint>();

        var routeNames = await GetRouteNamesAsync();

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
                    ServiceName = routeNames.TryGetValue(routeName, out var displayName) ? displayName : routeName,
                    TotalRequests = serviceTotal,
                    ErrorRequests = serviceErrors,
                    ErrorRate = serviceTotal > 0 ? (double)serviceErrors / serviceTotal * 100 : 0,
                    AverageResponseTime = serviceDurations.Count > 0 ? serviceDurations.Average(p => p.Value) : 0,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            });

        return serviceGroups.ToList();
    }

    /// <inheritdoc />
    public Task<IEnumerable<ErrorMetrics>> GetErrorMetricsAsync(DateTimeOffset start, DateTimeOffset end)
    {
        var errorPoints = _store.Metrics.TryGetValue("gateway.requests.errors", out var errors)
            ? errors.Where(p => p.Timestamp >= start.UtcDateTime && p.Timestamp <= end.UtcDateTime).ToList()
            : new List<MetricPoint>();

        // 按 (状态码, 路由) 双键分组，保留错误的完整路由归属
        var errorMetrics = errorPoints
            .GroupBy(p => (
                Status: p.Tags.TryGetValue("status", out var status) ? status?.ToString() ?? "unknown" : "unknown",
                Route: p.Tags.TryGetValue("route", out var route) ? route?.ToString() : null))
            .Select(g => new ErrorMetrics
            {
                Timestamp = end,
                ErrorCode = g.Key.Status,
                ErrorMessage = $"HTTP {(int.TryParse(g.Key.Status, out var code) ? code : 500)}",
                Count = g.Sum(p => (int)p.Value),
                RouteId = g.Key.Route,
                ClusterId = null
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
