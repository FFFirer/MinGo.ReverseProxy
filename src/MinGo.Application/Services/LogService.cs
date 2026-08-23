using Microsoft.Extensions.Logging;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using MinGo.Core.Services;

namespace MinGo.Application.Services;

public class LogService : ILogService
{
    private readonly TelemetryStore _store;
    private readonly ILogger<LogService> _logger;

    public LogService(TelemetryStore store, ILogger<LogService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<IEnumerable<AccessLog>> GetAccessLogsAsync(LogQuery query)
    {
        var logs = _store.AccessLogs.ToList();

        if (query.StatusCode.HasValue)
            logs = logs.Where(l => l.StatusCode == query.StatusCode.Value).ToList();
        if (!string.IsNullOrEmpty(query.RouteId))
            logs = logs.Where(l => l.Route.Contains(query.RouteId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrEmpty(query.Message))
            logs = logs.Where(l => l.Path.Contains(query.Message, StringComparison.OrdinalIgnoreCase)).ToList();

        var result = logs
            .OrderByDescending(l => l.Timestamp)
            .Take(query.PageSize)
            .Select(l => new AccessLog
            {
                Id = $"{l.Timestamp.Ticks}-{l.Path}",
                Timestamp = l.Timestamp,
                InstanceId = l.InstanceId ?? "",
                ClientIp = l.ClientIp,
                Method = l.Method,
                Path = l.Path,
                StatusCode = l.StatusCode,
                DurationMs = l.DurationMs,
                RouteId = l.Route
            });

        return Task.FromResult(result.AsEnumerable());
    }

    public Task<IEnumerable<ErrorLog>> GetErrorLogsAsync(LogQuery query)
    {
        var logs = _store.AccessLogs
            .Where(l => l.StatusCode >= 400)
            .ToList();

        if (!string.IsNullOrEmpty(query.RouteId))
            logs = logs.Where(l => l.Route.Contains(query.RouteId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (query.StatusCode.HasValue)
            logs = logs.Where(l => l.StatusCode == query.StatusCode.Value).ToList();

        var result = logs
            .OrderByDescending(l => l.Timestamp)
            .Take(query.PageSize)
            .Select(l => new ErrorLog
            {
                Id = $"{l.Timestamp.Ticks}-{l.Path}",
                Timestamp = l.Timestamp,
                InstanceId = l.InstanceId ?? "",
                Level = l.StatusCode >= 500 ? "Error" : "Warning",
                Category = "HTTP",
                Message = $"{l.StatusCode} {l.Method} {l.Path}",
                RouteId = l.Route
            });

        return Task.FromResult(result.AsEnumerable());
    }

    public Task<LogStatistics> GetLogStatisticsAsync(DateTimeOffset start, DateTimeOffset end)
    {
        var logs = _store.AccessLogs
            .Where(l => l.Timestamp >= start.UtcDateTime && l.Timestamp <= end.UtcDateTime)
            .ToList();

        var stats = new LogStatistics
        {
            TotalLogs = logs.Count,
            ErrorCount = logs.Count(l => l.StatusCode >= 500),
            WarningCount = logs.Count(l => l.StatusCode >= 400 && l.StatusCode < 500),
            InformationCount = logs.Count(l => l.StatusCode >= 200 && l.StatusCode < 400),
            DebugCount = 0,
            LevelDistribution = new Dictionary<string, int>
            {
                ["Error"] = logs.Count(l => l.StatusCode >= 500),
                ["Warning"] = logs.Count(l => l.StatusCode >= 400 && l.StatusCode < 500),
                ["Information"] = logs.Count(l => l.StatusCode >= 200 && l.StatusCode < 400)
            },
            CategoryDistribution = logs
                .GroupBy(l => l.Route)
                .ToDictionary(g => g.Key, g => g.Count()),
            InstanceDistribution = logs
                .GroupBy(l => l.InstanceId ?? "unknown")
                .ToDictionary(g => g.Key, g => g.Count())
        };

        return Task.FromResult(stats);
    }

    public async Task ExportLogsAsync(LogQuery query, Stream stream)
    {
        var logs = await GetAccessLogsAsync(query);
        using var writer = new StreamWriter(stream, leaveOpen: true);
        await writer.WriteLineAsync("Timestamp,InstanceId,Method,Path,StatusCode,DurationMs,ClientIp,RouteId");
        foreach (var log in logs)
        {
            await writer.WriteLineAsync($"{log.Timestamp:O},{log.InstanceId},{log.Method},{log.Path},{log.StatusCode},{log.DurationMs},{log.ClientIp},{log.RouteId}");
        }
        await writer.FlushAsync();
    }
}