namespace MinGo.Core.Models;

public class MetricsSummary
{
    public double AverageResponseTime { get; set; }
    public int TotalRequests { get; set; }
    public int ErrorRequests { get; set; }
    public double ErrorRate { get; set; }
    public int ActiveConnections { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}

public class RequestMetrics
{
    public DateTimeOffset Timestamp { get; set; }
    public int Count { get; set; }
    public int ErrorCount { get; set; }
    public double AverageResponseTime { get; set; }
    public double P95ResponseTime { get; set; }
    public double P99ResponseTime { get; set; }
}

public class ServiceMetrics
{
    public string ServiceId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public int TotalRequests { get; set; }
    public int ErrorRequests { get; set; }
    public double ErrorRate { get; set; }
    public double AverageResponseTime { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}

public class ErrorMetrics
{
    public DateTimeOffset Timestamp { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public int Count { get; set; }
    public string? RouteId { get; set; }
    public string? ClusterId { get; set; }
}

public class SystemMetrics
{
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public long DiskUsage { get; set; }
    public int NetworkIn { get; set; }
    public int NetworkOut { get; set; }
    public int ActiveProcesses { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}