using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Services;

public class LogService : ILogService
{
    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public Task<IEnumerable<AccessLog>> GetAccessLogsAsync(LogQuery query)
    {
        var logs = new List<AccessLog>
        {
            new AccessLog
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                Method = "GET",
                Path = "/api/users",
                ClientIp = "192.168.1.100",
                StatusCode = 200,
                ResponseTime = 32,
                ResponseSize = 1024,
                RouteId = "route1",
                ClusterId = "cluster1"
            },
            new AccessLog
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-4),
                Method = "POST",
                Path = "/api/products",
                ClientIp = "192.168.1.101",
                StatusCode = 201,
                ResponseTime = 45,
                ResponseSize = 2048,
                RouteId = "route2",
                ClusterId = "cluster2"
            },
            new AccessLog
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-3),
                Method = "GET",
                Path = "/api/orders",
                ClientIp = "192.168.1.102",
                StatusCode = 401,
                ResponseTime = 18,
                ResponseSize = 512,
                RouteId = "route3",
                ClusterId = "cluster3"
            }
        };
        
        if (!string.IsNullOrEmpty(query.RouteId))
        {
            logs = logs.Where(l => l.RouteId == query.RouteId).ToList();
        }
        
        if (!string.IsNullOrEmpty(query.ClusterId))
        {
            logs = logs.Where(l => l.ClusterId == query.ClusterId).ToList();
        }
        
        if (query.StatusCode.HasValue)
        {
            logs = logs.Where(l => l.StatusCode == query.StatusCode.Value).ToList();
        }
        
        return Task.FromResult<IEnumerable<AccessLog>>(logs);
    }

    public Task<IEnumerable<ErrorLog>> GetErrorLogsAsync(LogQuery query)
    {
        var logs = new List<ErrorLog>
        {
            new ErrorLog
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
                Level = "Error",
                Message = "Connection timeout",
                Exception = "System.TimeoutException",
                StackTrace = "at MinGo.Gateway.Services.ProxyService.ProxyAsync()",
                RouteId = "route1",
                ClusterId = "cluster1"
            },
            new ErrorLog
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                Level = "Error",
                Message = "Service unavailable",
                Exception = "System.ServiceModel.CommunicationException",
                StackTrace = "at MinGo.Gateway.Services.ProxyService.ProxyAsync()",
                RouteId = "route2",
                ClusterId = "cluster2"
            }
        };
        
        if (!string.IsNullOrEmpty(query.Level))
        {
            logs = logs.Where(l => l.Level == query.Level).ToList();
        }
        
        if (!string.IsNullOrEmpty(query.RouteId))
        {
            logs = logs.Where(l => l.RouteId == query.RouteId).ToList();
        }
        
        return Task.FromResult<IEnumerable<ErrorLog>>(logs);
    }

    public Task<LogStatistics> GetLogStatisticsAsync(DateTimeOffset start, DateTimeOffset end)
    {
        var statistics = new LogStatistics
        {
            TotalRequests = 12458,
            TotalErrors = 150,
            ErrorRate = 1.2,
            StatusCodes = new Dictionary<int, long>
            {
                { 200, 10000 },
                { 201, 2000 },
                { 401, 300 },
                { 404, 100 },
                { 500, 58 }
            },
            Routes = new Dictionary<string, long>
            {
                { "route1", 5000 },
                { "route2", 4000 },
                { "route3", 3458 }
            },
            Clusters = new Dictionary<string, long>
            {
                { "cluster1", 5000 },
                { "cluster2", 4000 },
                { "cluster3", 3458 }
            }
        };
        return Task.FromResult(statistics);
    }

    public Task ExportLogsAsync(LogQuery query, Stream stream)
    {
        _logger.LogInformation("Exporting logs for query: {@Query}", query);
        return Task.CompletedTask;
    }
}
