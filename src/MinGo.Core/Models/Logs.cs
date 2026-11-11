namespace MinGo.Core.Models;

public class LogQuery
{
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public string? Level { get; set; }
    public string? Category { get; set; }
    public string? Message { get; set; }
    public string? InstanceId { get; set; }
    public string? RouteId { get; set; }
    public string? ClusterId { get; set; }
    public int? StatusCode { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public class AccessLog
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string InstanceId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public string? UserAgent { get; set; }
    public string? RouteId { get; set; }
    public string? ClusterId { get; set; }
    public string? DestinationAddress { get; set; }

    /// <summary>代理目的地标识（不是地址）。老数据面未上报时为 null</summary>
    public string? DestinationId { get; set; }

    /// <summary>
    /// 链路关联键。控制面不存 span 本体，这个 id 用于在 OTLP 后端定位该请求的链路；
    /// 老数据面未上报时为 null，不能伪造
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>该请求在数据面的 span id；无 Activity 时数据面就不写，保持 null</summary>
    public string? SpanId { get; set; }
}

public class ErrorLog
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string InstanceId { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string? StackTrace { get; set; }
    public string? RouteId { get; set; }
    public string? ClusterId { get; set; }
}

public class LogStatistics
{
    public int TotalLogs { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InformationCount { get; set; }
    public int DebugCount { get; set; }
    public Dictionary<string, int> LevelDistribution { get; set; } = new();
    public Dictionary<string, int> CategoryDistribution { get; set; } = new();
    public Dictionary<string, int> InstanceDistribution { get; set; } = new();
    public List<LogTrend> Trends { get; set; } = new();
}

public class LogTrend
{
    public DateTimeOffset Timestamp { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InformationCount { get; set; }
    public int DebugCount { get; set; }
}