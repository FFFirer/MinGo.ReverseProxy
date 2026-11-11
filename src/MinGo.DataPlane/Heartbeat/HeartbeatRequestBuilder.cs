using Microsoft.Extensions.Logging;
using MinGo.Core.Services;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Grpc;
using CoreMetricPoint = MinGo.Core.Services.MetricPoint;
using CoreAccessLogEntry = MinGo.Core.Services.AccessLogEntry;
using GrpcAccessLogEntry = MinGo.DataPlane.Grpc.AccessLogEntry;

namespace MinGo.DataPlane.Heartbeat;

/// <summary>
/// 心跳请求的构造器 —— 从 <see cref="HeartbeatReporter"/> 里抽出来，让"上报了什么"第一次成为可测的行为。
///
/// 它承担的三件事，每一件都对应一个曾经静默失效的错误：
/// 1. 按游标增量抽取指标点，取代"每个指标只取最后 10 个点"的系统性欠报；
/// 2. 抽不到上限时顺延而不是丢弃，顺延与丢失是两件事；
/// 3. 累计计数、CPU、内存、健康状态来自真实来源，而不是硬编码常量。
///
/// 提交时机决定送达语义：<see cref="Build"/> 只取页，<see cref="Commit"/> 才推进游标。
/// 写流失败就不提交，下一周期重取同一页 —— 至多重复一个周期，而不是丢掉一段数据。
/// </summary>
public sealed class HeartbeatRequestBuilder
{
    private readonly TelemetryStore _store;
    private readonly GatewayIdentity _identity;
    private readonly IProcessSampler _sampler;
    private readonly DataPlaneConfigProvider _configProvider;
    private readonly Func<(long Total, long Errors)> _lifetimeTotals;
    private readonly ILogger<HeartbeatRequestBuilder> _logger;

    private readonly int _maxMetricPointsPerName;
    private readonly int _maxAccessLogs;

    /// <summary>已确认送达的游标</summary>
    private readonly Dictionary<string, long> _cursors = new(StringComparer.Ordinal);

    /// <summary>本次 Build 取出的页尾，Commit 后才并入 <see cref="_cursors"/></summary>
    private readonly Dictionary<string, long> _pendingCursors = new(StringComparer.Ordinal);

    /// <summary>已取出但尚未确认送达的访问日志（Drain 是破坏性的，必须自己兜住）</summary>
    private List<CoreAccessLogEntry> _pendingAccessLogs = new();

    /// <summary>累计"未上报就因保留窗口驱逐而丢失"的指标点数</summary>
    private long _unreportedDropped;

    public HeartbeatRequestBuilder(
        TelemetryStore store,
        GatewayIdentity identity,
        IProcessSampler sampler,
        DataPlaneConfigProvider configProvider,
        Func<(long Total, long Errors)> lifetimeTotals,
        int maxMetricPointsPerName,
        int maxAccessLogs,
        ILogger<HeartbeatRequestBuilder> logger)
    {
        _store = store;
        _identity = identity;
        _sampler = sampler;
        _configProvider = configProvider;
        _lifetimeTotals = lifetimeTotals;
        _maxMetricPointsPerName = maxMetricPointsPerName > 0 ? maxMetricPointsPerName : 2000;
        _maxAccessLogs = maxAccessLogs > 0 ? maxAccessLogs : 200;
        _logger = logger;
    }

    /// <summary>尚未确认送达的访问日志条数（观测顺延量与测试断言用）</summary>
    public int PendingAccessLogCount => _pendingAccessLogs.Count;

    /// <summary>
    /// 构造一次心跳请求。不推进游标 —— 调用方必须在写流成功后调用 <see cref="Commit"/>。
    /// </summary>
    public MinGo.DataPlane.Grpc.HeartbeatRequest Build()
    {
        var totals = _lifetimeTotals();
        var usage = _sampler.Sample();

        var request = new Grpc.HeartbeatRequest
        {
            DataPlaneId = _identity.Id,
            CpuUsage = usage.CpuPercent,
            MemoryUsage = usage.MemoryMegabytes,
            TotalRequests = totals.Total,
            ErrorRequests = totals.Errors,
            // 与 /healthz/ready 同一判据：两处若不一致，运维看到的"健康"就取决于它先看哪一块屏
            IsHealthy = _configProvider.LastApplySucceeded,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        AppendAccessLogs(request);
        AppendMetricPages(request);
        AppendSelfObservedCounters(request);

        return request;
    }

    /// <summary>确认这一页已经送达，推进游标并丢弃已送达的访问日志</summary>
    public void Commit()
    {
        foreach (var (name, sequence) in _pendingCursors)
        {
            _cursors[name] = sequence;
        }

        _pendingCursors.Clear();
        _pendingAccessLogs = new List<CoreAccessLogEntry>();
    }

    private void AppendAccessLogs(Grpc.HeartbeatRequest request)
    {
        // 上次没送出去的排在前面，保持时间顺序；Drain 已经把它们移出 store，不能再丢
        var page = _pendingAccessLogs;
        var room = _maxAccessLogs - page.Count;
        if (room > 0)
        {
            page.AddRange(_store.DrainAccessLogs(room));
        }

        foreach (var log in page)
        {
            request.AccessLogs.Add(ToGrpc(log));
        }
    }

    private void AppendMetricPages(Grpc.HeartbeatRequest request)
    {
        _pendingCursors.Clear();

        // 指标名集合有界（三个请求指标 + 自观测计数），排序只为让每拍顺序稳定、便于比对
        foreach (var name in _store.Metrics.Keys.OrderBy(static n => n, StringComparer.Ordinal))
        {
            if (IsSelfObservedCounter(name))
            {
                continue;
            }

            AppendMetricPage(request, name);
        }
    }

    /// <summary>
    /// 自观测计数每拍写一次累计值，再立即抽取，使本轮发现的丢失本轮就上报。
    /// </summary>
    private void AppendSelfObservedCounters(Grpc.HeartbeatRequest request)
    {
        var timestamp = DateTime.UtcNow;

        _store.AddMetric(TelemetryStore.UnreportedDroppedMetricName, new CoreMetricPoint
        {
            Timestamp = timestamp,
            Value = _unreportedDropped,
            Tags = new Dictionary<string, object>(0)
        });

        _store.AddMetric(TelemetryStore.AccessLogDroppedMetricName, new CoreMetricPoint
        {
            Timestamp = timestamp,
            Value = _store.DroppedAccessLogs,
            Tags = new Dictionary<string, object>(0)
        });

        // 三个自观测指标都要出去：本地驱逐计数由 store 自己产生（上面不重复记录），
        // 漏掉它等于"存不下了没人知道"——那正是阶段一要消灭的那类静默
        AppendMetricPage(request, TelemetryStore.DroppedMetricName);
        AppendMetricPage(request, TelemetryStore.UnreportedDroppedMetricName);
        AppendMetricPage(request, TelemetryStore.AccessLogDroppedMetricName);
    }

    private static bool IsSelfObservedCounter(string name) =>
        name == TelemetryStore.DroppedMetricName
        || name == TelemetryStore.UnreportedDroppedMetricName
        || name == TelemetryStore.AccessLogDroppedMetricName;

    private void AppendMetricPage(Grpc.HeartbeatRequest request, string name)
    {
        var cursor = _cursors.TryGetValue(name, out var existing) ? existing : 0;

        // 未提交时不要重复取页：Build 后未 Commit 就又 Build（写失败重试）应重放同一页
        var page = _store.ReadMetricPointsAfter(name, cursor, _maxMetricPointsPerName);
        if (page.Count == 0)
        {
            return;
        }

        // 序号缺口 = 游标之后、本页最早点之前已经不在 store 里的点。
        // 那就是"还没上报就被保留窗口驱逐"，是本变更要变成可见数字的那类丢失。
        var gap = page[0].Sequence - cursor - 1;
        if (gap > 0)
        {
            _unreportedDropped += gap;
            _logger.LogWarning(
                "Metric {Metric} lost {Count} points before reporting (cursor {Cursor}, oldest available {Oldest})",
                name, gap, cursor, page[0].Sequence);
        }

        foreach (var point in page)
        {
            request.Metrics.Add(ToGrpc(name, point));
        }

        _pendingCursors[name] = page[^1].Sequence;

        var backlog = page.Count == _maxMetricPointsPerName;
        if (backlog)
        {
            _logger.LogInformation(
                "Metric {Metric} page filled its cap ({Limit}); the rest is deferred to the next heartbeat",
                name, _maxMetricPointsPerName);
        }
    }

    private static Grpc.MetricPoint ToGrpc(string name, CoreMetricPoint point)
    {
        var grpcPoint = new Grpc.MetricPoint
        {
            Name = name,
            Value = point.Value,
            TimestampUnixMs = new DateTimeOffset(point.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds()
        };

        foreach (var (key, val) in point.Tags)
        {
            grpcPoint.Tags[key] = val?.ToString() ?? "";
        }

        return grpcPoint;
    }

    private static GrpcAccessLogEntry ToGrpc(CoreAccessLogEntry log) => new()
    {
        TimestampUnixMs = new DateTimeOffset(log.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds(),
        Method = log.Method,
        Path = log.Path,
        StatusCode = log.StatusCode,
        DurationMs = log.DurationMs,
        ClientIp = log.ClientIp,
        Route = log.Route,
        // 阶段二建立的关联键在这里跨过进程边界。null 一律成空串：proto3 没有可选字符串，
        // "没有值"与"值是不存在的维度哨兵 none"必须由接收方区分，采集侧不替传输层编造。
        TraceId = log.TraceId ?? "",
        SpanId = log.SpanId ?? "",
        Cluster = log.Cluster ?? "",
        Destination = log.Destination ?? ""
    };
}
