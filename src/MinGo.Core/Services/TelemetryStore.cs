using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace MinGo.Core.Services
{
    /// <summary>
    /// 遥测数据存储
    /// </summary>
    public class TelemetryStore
    {
        /// <summary>
        /// 最大跟踪数量
        /// 与其他容量上限一样公开，便于测试与运维核对本地 trace 队列的边界
        /// </summary>
        public const int MaxTraces = 2000;

        /// <summary>
        /// 指标与访问日志的保留时长（小时）
        /// </summary>
        private const int RetentionHours = 24;

        /// <summary>
        /// 每个指标名的默认点数上限
        /// </summary>
        public const int DefaultMaxPointsPerMetric = 50000;

        /// <summary>
        /// 驱逐计数的自观测指标名（固定标签，不参与驱逐记录以避免递归）
        /// </summary>
        public const string DroppedMetricName = "gateway.telemetry.dropped";

        /// <summary>
        /// 访问日志默认条数上限
        /// </summary>
        public const int DefaultMaxAccessLogs = 5000;

        /// <summary>
        /// 未被上报就被保留窗口驱逐的指标点数（累计值，固定空标签）
        /// 与 <see cref="DroppedMetricName"/> 的区别：那个是"本地存不下"，这个是"心跳没来得及报"
        /// </summary>
        public const string UnreportedDroppedMetricName = "gateway.telemetry.unreported.dropped";

        /// <summary>
        /// 因超出访问日志条数上限而被丢弃的条目数（累计值，固定空标签）
        /// </summary>
        public const string AccessLogDroppedMetricName = "gateway.telemetry.access_log.dropped";

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

        private readonly int _maxPointsPerMetric;
        private readonly int _maxAccessLogs;

        /// <summary>
        /// 队列长度计数。ConcurrentQueue.Count 是 O(n) 快照，不能用于每请求热路径的裁剪判定，
        /// 因此这里由每次入队/出队自行记账；所有对指标与访问日志队列的增删都发生在本类内部。
        /// </summary>
        private readonly ConcurrentDictionary<string, int> _metricCounts = new();

        /// <summary>
        /// 每个指标名已分配的点数序号。序号在入队前分配，因此同一指标的并发写入理论上可能
        /// 出现极短暂的序号与队列位置不一致；读取侧按序号排序后再截页，游标不会跳过尚未报出的点。
        /// </summary>
        private readonly ConcurrentDictionary<string, long> _metricSequences = new();

        private int _accessLogCount;

        private int _traceCount;

        private long _droppedMetricPoints;

        private long _droppedAccessLogs;

        /// <summary>
        /// 使用默认容量上限
        /// </summary>
        public TelemetryStore() : this(DefaultMaxPointsPerMetric, DefaultMaxAccessLogs)
        {
        }

        /// <summary>
        /// 使用指定容量上限
        /// </summary>
        public TelemetryStore(int maxPointsPerMetric, int maxAccessLogs = DefaultMaxAccessLogs)
        {
            _maxPointsPerMetric = maxPointsPerMetric > 0 ? maxPointsPerMetric : DefaultMaxPointsPerMetric;
            _maxAccessLogs = maxAccessLogs > 0 ? maxAccessLogs : DefaultMaxAccessLogs;
        }

        /// <summary>
        /// 累计因超出容量上限而被驱逐的指标点数
        /// </summary>
        public long DroppedMetricPoints => Interlocked.Read(ref _droppedMetricPoints);

        /// <summary>
        /// 生效的每指标点数上限
        /// </summary>
        public int MaxPointsPerMetric => _maxPointsPerMetric;

        /// <summary>
        /// 生效的访问日志条数上限
        /// </summary>
        public int MaxAccessLogs => _maxAccessLogs;

        /// <summary>
        /// 添加跟踪数据
        /// 本地 trace exporter 使这里成为每请求路径，因此裁剪判定使用记账计数，
        /// 不使用 O(n) 的 ConcurrentQueue.Count
        /// </summary>
        /// <param name="activity">活动对象</param>
        public void AddTrace(Activity activity)
        {
            Traces.Enqueue(activity);
            var count = Interlocked.Increment(ref _traceCount);

            while (count > MaxTraces)
            {
                if (!Traces.TryDequeue(out _))
                {
                    break;
                }

                count = Interlocked.Decrement(ref _traceCount);
            }
        }

        /// <summary>
        /// 累计因超出访问日志条数上限而被丢弃的条目数
        /// </summary>
        public long DroppedAccessLogs => Interlocked.Read(ref _droppedAccessLogs);

        /// <summary>
        /// 添加指标数据
        /// 序号在入队前分配，供心跳按游标增量抽取（见 <see cref="ReadMetricPointsAfter"/>）。
        /// 控制面 ingest 自行构造的点不依赖序号，默认值 0 只存在于对端。
        /// </summary>
        /// <param name="name">指标名称</param>
        /// <param name="point">指标点</param>
        public void AddMetric(string name, MetricPoint point)
        {
            var queue = Enqueue(name, point);
            var count = _metricCounts.AddOrUpdate(name, 1, static (_, existing) => existing + 1);

            TrimMetrics(name, queue, ref count);
        }

        /// <summary>
        /// 盖序号并入队。所有写入路径都必须走这里 ——
        /// 绕过它（例如直接 Enqueue）会让点带着序号 0 进队列，
        /// 而心跳的游标读取只看序号大于游标的点，那些点永远不会被上报。
        /// </summary>
        private ConcurrentQueue<MetricPoint> Enqueue(string name, MetricPoint point)
        {
            var queue = Metrics.GetOrAdd(name, _ => new ConcurrentQueue<MetricPoint>());
            point.Sequence = _metricSequences.AddOrUpdate(name, 1, static (_, existing) => existing + 1);
            queue.Enqueue(point);
            return queue;
        }

        /// <summary>
        /// 读取序号大于游标的指标点，按序号升序，最多 maxCount 个。
        /// 这是每 10 秒的心跳路径而不是每请求路径，因此允许枚举；
        /// 返回值不承诺连续——首个序号与游标的差即"未上报就被驱逐"的条数。
        /// </summary>
        public List<MetricPoint> ReadMetricPointsAfter(string name, long afterSequence, int maxCount)
        {
            if (maxCount <= 0 || !Metrics.TryGetValue(name, out var queue))
            {
                return new List<MetricPoint>();
            }

            // 枚举器给出调用那一刻的快照：快照形成后头部被驱逐的点不会出现在视图里，
            // 于是丢失表现为序号缺口，而不是静默少报。
            // 上限来自配置，不照单预分配；页容量上限只是提示性的
            var page = new List<MetricPoint>(Math.Min(maxCount, 4096));
            foreach (var point in queue)
            {
                if (point.Sequence > afterSequence)
                {
                    page.Add(point);
                }
            }

            // 并发入队可能让序号与队列位置短暂不一致，排序后截页才能保证游标不跳过更旧的点
            page.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));

            if (page.Count > maxCount)
            {
                page.RemoveRange(maxCount, page.Count - maxCount);
            }

            return page;
        }

        /// <summary>
        /// 按保留时长与容量上限裁剪指标点，超出上限时驱逐最旧的点并累计驱逐计数
        /// </summary>
        private void TrimMetrics(string name, ConcurrentQueue<MetricPoint> queue, ref int count)
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-RetentionHours);
            var evicted = 0;

            while (true)
            {
                var overCapacity = count > _maxPointsPerMetric;
                var expired = queue.TryPeek(out var oldest) && oldest.Timestamp < cutoffTime;
                if (!overCapacity && !expired)
                {
                    break;
                }

                if (!queue.TryDequeue(out _))
                {
                    break;
                }

                _metricCounts.AddOrUpdate(name, 0, static (_, existing) => existing - 1);
                count--;
                if (overCapacity)
                {
                    evicted++;
                }
            }

            // 自观测指标本身不再记录驱逐，避免递归
            if (evicted > 0 && name != DroppedMetricName)
            {
                RecordEviction(evicted);
            }
        }

        /// <summary>
        /// 记录驱逐计数：值为累计驱逐点数，标签固定，不携带 route/cluster/status
        /// </summary>
        private void RecordEviction(int evicted)
        {
            var total = Interlocked.Add(ref _droppedMetricPoints, evicted);

            var queue = Enqueue(DroppedMetricName, new MetricPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = total,
                Tags = new Dictionary<string, object>(0)
            });

            var count = _metricCounts.AddOrUpdate(DroppedMetricName, 1, static (_, existing) => existing + 1);
            TrimMetrics(DroppedMetricName, queue, ref count);
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
        /// 保留策略与原实现一致：仅按条数做 FIFO 裁剪（心跳会周期性取走），
        /// 判定使用记账计数而非 O(n) 的 ConcurrentQueue.Count，
        /// 被丢弃的条目数累计到 <see cref="DroppedAccessLogs"/>，使"报不过来"不再是静默丢失
        /// </summary>
        public void AddAccessLog(AccessLogEntry entry)
        {
            AccessLogs.Enqueue(entry);
            var count = Interlocked.Increment(ref _accessLogCount);

            while (count > _maxAccessLogs)
            {
                if (!AccessLogs.TryDequeue(out _))
                {
                    break;
                }

                Interlocked.Increment(ref _droppedAccessLogs);
                count = Interlocked.Decrement(ref _accessLogCount);
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
                Interlocked.Decrement(ref _accessLogCount);
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
        public string? InstanceId { get; set; }

        /// <summary>访问域名（Host 头），多域名网关区分流量来源用</summary>
        public string Host { get; set; } = "";

        /// <summary>等待上游响应时间（请求开始到响应头发出，含上游连接与处理）</summary>
        public long TtfbMs { get; set; }

        /// <summary>响应体传输时间（首字节到末字节写入）</summary>
        public long DownloadMs { get; set; }

        /// <summary>代理维度：目标集群；未走反向代理时为采集侧的 none 哨兵</summary>
        public string? Cluster { get; set; }

        /// <summary>代理维度：被选中的目的地；没有目的地被选中时为采集侧的 none 哨兵</summary>
        public string? Destination { get; set; }

        /// <summary>
        /// 链路关联键：有 Activity 时为 W3C TraceId，否则回退 ASP.NET Core TraceIdentifier。
        /// 随心跳上报到控制面（proto AccessLogEntry.trace_id），因此跨进程仍可回查链路。
        /// </summary>
        public string? TraceId { get; set; }

        /// <summary>关联的 span id，无 Activity 时为 null</summary>
        public string? SpanId { get; set; }
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
        /// 该指标名下的单调序号，由 <see cref="TelemetryStore.AddMetric"/> 分配。
        /// 心跳上报据此做增量抽取；跨进程传来的点没有本地语义，保持 0。
        /// </summary>
        public long Sequence { get; set; }

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
