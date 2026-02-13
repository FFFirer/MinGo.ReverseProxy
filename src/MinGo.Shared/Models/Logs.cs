namespace MinGo.Shared.Models;

public class AccessLog
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? QueryString { get; set; }
    public string? UserAgent { get; set; }
    public string? ClientIp { get; set; }
    public int StatusCode { get; set; }
    public long ResponseTime { get; set; }
    public long ResponseSize { get; set; }
    public string? RouteId { get; set; }
    public string? ClusterId { get; set; }
}

public class ErrorLog
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string? StackTrace { get; set; }
    public string? RouteId { get; set; }
    public string? ClusterId { get; set; }
    public Dictionary<string, string>? Properties { get; set; }
}

public class LogQuery
{
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public string? RouteId { get; set; }
    public string? ClusterId { get; set; }
    public int? StatusCode { get; set; }
    public string? Level { get; set; }
    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class LogStatistics
{
    public long TotalRequests { get; set; }
    public long TotalErrors { get; set; }
    public double ErrorRate { get; set; }
    public Dictionary<int, long> StatusCodes { get; set; } = new();
    public Dictionary<string, long> Routes { get; set; } = new();
    public Dictionary<string, long> Clusters { get; set; } = new();
}
