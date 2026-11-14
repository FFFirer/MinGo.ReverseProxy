using System.Collections.Concurrent;
using System.Diagnostics;

namespace MinGo.Core.Services
{
    /// <summary>
    /// 遥测数据存储
    /// </summary>
    public class TelemetryStore
    {
        /// <summary>
        /// 最大跟踪数量
        /// </summary>
        private const int MaxTraces = 2000;

        /// <summary>
        /// 跟踪数据队列
        /// </summary>
        public ConcurrentQueue<Activity> Traces { get; } = new();

        /// <summary>
        /// 指标数据字典
        /// </summary>
        public ConcurrentDictionary<string, ConcurrentQueue<MetricPoint>> Metrics { get; } = new();

        /// <summary>
        /// 日志数据环形缓冲区
        /// </summary>
        public ConcurrentQueue<string> Logs { get; } = new();

        /// <summary>
        /// 结构化访问日志环形缓冲区
        /// </summary>
        public ConcurrentQueue<AccessLogEntry> AccessLogs { get; } = new();

        private long _totalRequests;
        private long _errorRequests;

        /// <summary>
        /// 累计请求总数（心跳上报用，不随指标队列 drain 清零）
        /// </summary>
        public long TotalRequests => Interlocked.Read(ref _totalRequests);

        /// <summary>
        /// 累计错误请求数（状态码 >= 400）
        /// </summary>
        public long ErrorRequests => Interlocked.Read(ref _errorRequests);

        /// <summary>
        /// 记录一次请求的累计计数
        /// </summary>
        public void IncrementCounters(int statusCode)
        {
            Interlocked.Increment(ref _totalRequests);
            if (statusCode >= 400)
            {
                Interlocked.Increment(ref _errorRequests);
            }
        }

        /// <summary>
        /// 添加跟踪数据
        /// </summary>
        /// <param name="activity">活动对象</param>
        public void AddTrace(Activity activity)
        {
            Traces.Enqueue(activity);
            while (Traces.Count > MaxTraces)
            {
                Traces.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 添加指标数据
        /// </summary>
        /// <param name="name">指标名称</param>
        /// <param name="point">指标点</param>
        public void AddMetric(string name, MetricPoint point)
        {
            var queue = Metrics.GetOrAdd(name, _ => new ConcurrentQueue<MetricPoint>());
            queue.Enqueue(point);

            // 限制指标数据点数量（24小时）
            var cutoffTime = DateTime.UtcNow.AddHours(-24);
            while (queue.TryPeek(out var oldest) && oldest.Timestamp < cutoffTime)
            {
                queue.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 添加日志数据
        /// </summary>
        /// <param name="log">日志内容</param>
        public void AddLog(string log)
        {
            Logs.Enqueue(log);
            while (Logs.Count > 10000)
            {
                Logs.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 添加访问日志
        /// </summary>
        public void AddAccessLog(AccessLogEntry entry)
        {
            AccessLogs.Enqueue(entry);
            while (AccessLogs.Count > 5000)
            {
                AccessLogs.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 批量取出并清空访问日志（用于心跳上报）
        /// </summary>
        public List<AccessLogEntry> DrainAccessLogs(int maxCount = 200)
        {
            var result = new List<AccessLogEntry>();
            while (result.Count < maxCount && AccessLogs.TryDequeue(out var entry))
            {
                result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// 批量取出并清空指标点（用于心跳上报）。
        /// 与访问日志相同的 drain 语义：每个点只上报一次，避免控制面重复累加。
        /// </summary>
        public List<(string Name, MetricPoint Point)> DrainMetrics(int maxCount = 5000)
        {
            var result = new List<(string, MetricPoint)>();
            foreach (var (name, queue) in Metrics)
            {
                while (result.Count < maxCount && queue.TryDequeue(out var point))
                {
                    result.Add((name, point));
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 访问日志条目
    /// </summary>
    public class AccessLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Method { get; set; } = "";
        public string Path { get; set; } = "";
        public int StatusCode { get; set; }
        public long DurationMs { get; set; }
        public string ClientIp { get; set; } = "";
        public string Route { get; set; } = "";
        public string Host { get; set; } = "";
        public string Cluster { get; set; } = "";
        public string? InstanceId { get; set; }

        /// <summary>
        /// 等待上游响应时间（请求开始到响应头发出，含上游连接与处理）
        /// </summary>
        public long TtfbMs { get; set; }

        /// <summary>
        /// 响应体传输时间（首字节到末字节写入）
        /// </summary>
        public long DownloadMs { get; set; }
    }

    /// <summary>
    /// 指标点
    /// </summary>
    public class MetricPoint
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 值
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// 标签
        /// </summary>
        public Dictionary<string, object> Tags { get; set; } = new();
    }
}