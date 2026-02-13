namespace MinGo.Shared.Models;

public class MetricsSummary
{
    public long TotalRequests { get; set; }
    public double ErrorRate { get; set; }
    public double AverageResponseTime { get; set; }
    public int ActiveServices { get; set; }
    public int TotalServices { get; set; }
}

public class RequestMetrics
{
    public string RouteId { get; set; } = string.Empty;
    public long RequestCount { get; set; }
    public double AverageResponseTime { get; set; }
    public double ErrorRate { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public class ServiceMetrics
{
    public string ClusterId { get; set; } = string.Empty;
    public string DestinationId { get; set; } = string.Empty;
    public bool Healthy { get; set; }
    public long RequestCount { get; set; }
    public double AverageResponseTime { get; set; }
    public double ErrorRate { get; set; }
}

public class ErrorMetrics
{
    public string RouteId { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public long ErrorCount { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

public class SystemMetrics
{
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double NetworkUsage { get; set; }
    public int ActiveConnections { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
