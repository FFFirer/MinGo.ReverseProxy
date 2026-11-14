using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MinGo.Core.Services;

namespace MinGo.Core.Tests;

/// <summary>
/// 单元验证：TelemetryStore 的容量上限与驱逐可观测性
/// 契约见 request-telemetry 规范 "TelemetryStore SHALL bound stored metric points"
/// </summary>
public class TelemetryStoreTests
{
    private static MetricPoint Point(DateTime? timestamp = null) => new()
    {
        Timestamp = timestamp ?? DateTime.UtcNow,
        Value = 1,
        Tags = new Dictionary<string, object>()
    };

    private static MetricPoint Point(double value) => new()
    {
        Timestamp = DateTime.UtcNow,
        Value = value,
        Tags = new Dictionary<string, object>()
    };

    [Fact]
    public void AddMetric_BeyondCap_EvictsOldestAndStaysBounded()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 100);

        for (var i = 0; i < 250; i++)
        {
            store.AddMetric("test.metric", Point(i));
        }

        var queue = store.Metrics["test.metric"];
        Assert.Equal(100, queue.Count);
        Assert.Equal(150, store.DroppedMetricPoints);

        // 驱逐的是最旧的点：保留的应为 150..249
        Assert.Equal(150d, queue.First().Value);
        Assert.Equal(249d, queue.Last().Value);
    }

    [Fact]
    public void Eviction_ExposedAsSelfObservingMetric()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 10);

        for (var i = 0; i < 30; i++)
        {
            store.AddMetric("test.metric", Point());
        }

        Assert.True(store.Metrics.ContainsKey(TelemetryStore.DroppedMetricName));
        var droppedPoint = store.Metrics[TelemetryStore.DroppedMetricName].Last();
        Assert.Equal(20d, droppedPoint.Value);
        // 自观测指标使用固定标签，不携带 route/cluster/status，避免自身引入基数
        Assert.Empty(droppedPoint.Tags);
    }

    [Fact]
    public void EvictionCounter_DoesNotRecurse_WhenDroppedMetricItselfIsCapped()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 5);

        // 单次写入触发驱逐 => 记录 dropped 指标；dropped 指标继续写入不应再触发记录
        for (var i = 0; i < 60; i++)
        {
            store.AddMetric("test.metric", Point());
        }

        Assert.Equal(5, store.Metrics[TelemetryStore.DroppedMetricName].Count);
        Assert.Equal(55, store.DroppedMetricPoints);
    }

    [Fact]
    public void MultipleMetrics_EachHasOwnCap()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 20);

        for (var i = 0; i < 50; i++)
        {
            store.AddMetric("a", Point());
            store.AddMetric("b", Point());
        }

        Assert.Equal(20, store.Metrics["a"].Count);
        Assert.Equal(20, store.Metrics["b"].Count);
    }

    [Fact]
    public void AddMetric_ExpiredPoints_TrimmedByTimeWindow()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 1000);
        var old = DateTime.UtcNow.AddHours(-25);

        store.AddMetric("test.metric", Point(old));
        store.AddMetric("test.metric", Point(old));
        store.AddMetric("test.metric", Point());

        var remaining = store.Metrics["test.metric"].ToList();
        Assert.Single(remaining);
        // 时间裁剪不计入驱逐（属于保留窗口到期，不是容量驱逐）
        Assert.Equal(0, store.DroppedMetricPoints);
    }

    [Fact]
    public void AddAccessLog_BeyondCap_EvictsOldest()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 1000, maxAccessLogs: 10);

        for (var i = 0; i < 30; i++)
        {
            store.AddAccessLog(new AccessLogEntry { Path = $"/p{i}", StatusCode = 200 });
        }

        Assert.Equal(10, store.AccessLogs.Count);
        Assert.Equal("/p20", store.AccessLogs.First().Path);
    }

    [Fact]
    public void DrainAccessLogs_KeepsCountBookkeepingConsistent()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 1000, maxAccessLogs: 100);

        for (var i = 0; i < 50; i++)
        {
            store.AddAccessLog(new AccessLogEntry { Path = $"/p{i}" });
        }

        var drained = store.DrainAccessLogs(20);
        Assert.Equal(20, drained.Count);
        Assert.Equal(30, store.AccessLogs.Count);

        // 记账正确：继续写入不应因计数漂移而提前驱逐
        store.AddAccessLog(new AccessLogEntry { Path = "/fresh" });
        Assert.Equal(31, store.AccessLogs.Count);
    }

    [Fact]
    public void Constructor_NonPositiveCap_FallsBackToDefault()
    {
        var store = new TelemetryStore(0);

        for (var i = 0; i < TelemetryStore.DefaultMaxPointsPerMetric + 10; i++)
        {
            store.AddMetric("test.metric", Point());
        }

        Assert.Equal(TelemetryStore.DefaultMaxPointsPerMetric, store.Metrics["test.metric"].Count);
    }

    [Fact]
    public void AddTrace_BeyondCap_EvictsOldestAndStaysBounded()
    {
        // 本地 trace exporter 让这条路径进入每请求热路径，上限必须硬生效
        var store = new TelemetryStore(maxPointsPerMetric: 1000);
        var total = TelemetryStore.MaxTraces + 100;

        for (var i = 0; i < total; i++)
        {
            store.AddTrace(new Activity($"op-{i}"));
        }

        Assert.Equal(TelemetryStore.MaxTraces, store.Traces.Count);
        Assert.Equal($"op-{total - TelemetryStore.MaxTraces}", store.Traces.First().OperationName);
        Assert.Equal($"op-{total - 1}", store.Traces.Last().OperationName);
    }

    [Fact]
    public void AddTrace_BelowCap_KeepsEveryTraceInOrder()
    {
        // 未触顶时不得有任何丢失或乱序：本地 trace 看板读的就是这条队列
        var store = new TelemetryStore(maxPointsPerMetric: 1000);

        for (var i = 0; i < 10; i++)
        {
            store.AddTrace(new Activity($"trace-{i}"));
        }

        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"trace-{i}"),
            store.Traces.Select(a => a.OperationName));
    }

    /// <summary>
    /// 控制面以 AddSingleton&lt;TelemetryStore&gt;() 注册（未显式指定容量）。
    /// 新增带参构造后，依赖注入仍必须能解析出无参重载，否则控制面启动即失败。
    /// </summary>
    [Fact]
    public void DependencyInjection_ResolvesParameterlessConstructor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TelemetryStore>();

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<TelemetryStore>();

        Assert.NotNull(store);
        Assert.Equal(TelemetryStore.DefaultMaxPointsPerMetric, store.MaxPointsPerMetric);
    }

    /// <summary>
    /// 契约见 telemetry-reporting 规范 "Heartbeat metric reporting SHALL cover every point retained by the data plane"：
    /// 游标之后的点必须全部可读，否则心跳只能抽样。
    /// </summary>
    [Fact]
    public void ReadMetricPointsAfter_AfterNewerPointsExist_ReturnsEveryPendingPointOldToNew()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 50000);

        for (var i = 1; i <= 30; i++)
        {
            store.AddMetric("test.metric", Point(i));
        }

        var all = store.ReadMetricPointsAfter("test.metric", afterSequence: 0, maxCount: 2000);

        Assert.Equal(30, all.Count);
        Assert.Equal(Enumerable.Range(1, 30), all.Select(p => (int)p.Value));
        Assert.Equal(Enumerable.Range(1, 30), all.Select(p => (int)p.Sequence));
    }

    [Fact]
    public void ReadMetricPointsAfter_AfterTheNewestPoint_ReturnsNothing()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 50000);
        store.AddMetric("test.metric", Point(1));

        var page = store.ReadMetricPointsAfter("test.metric", store.Metrics["test.metric"].Last().Sequence, 2000);

        Assert.Empty(page);
    }

    [Fact]
    public void ReadMetricPointsAfter_CursorBeyondNewest_StillReturnsNothingRatherThanEverything()
    {
        // 游标比最新序号还大只可能是状态错乱（例如 store 被换实例）。
        // 这里必须返回空而不是"全部"，否则一次错位就变成整段重报。
        var store = new TelemetryStore(maxPointsPerMetric: 50000);
        store.AddMetric("test.metric", Point(1));

        Assert.Empty(store.ReadMetricPointsAfter("test.metric", afterSequence: 9_999, maxCount: 2000));
    }

    [Fact]
    public void ReadMetricPointsAfter_RespectsMaxCountWithTheOldestPoints()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 50000);
        for (var i = 1; i <= 50; i++)
        {
            store.AddMetric("test.metric", Point(i));
        }

        var page = store.ReadMetricPointsAfter("test.metric", afterSequence: 0, maxCount: 7);

        Assert.Equal(Enumerable.Range(1, 7), page.Select(p => (int)p.Value));
    }

    [Fact]
    public void ReadMetricPointsAfter_UnknownMetric_ReturnsEmpty()
    {
        var store = new TelemetryStore();

        Assert.Empty(store.ReadMetricPointsAfter("never.recorded", 0, 10));
    }

    [Fact]
    public void ReadMetricPointsAfter_PointAfterCursor_SkipsAlreadyReportedPrefix()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 50000);
        for (var i = 1; i <= 10; i++)
        {
            store.AddMetric("test.metric", Point(i));
        }

        // 模拟心跳：先报 4 个，提交游标，再取剩余
        var first = store.ReadMetricPointsAfter("test.metric", 0, 4);
        var second = store.ReadMetricPointsAfter("test.metric", first[^1].Sequence, 2000);

        Assert.Equal(Enumerable.Range(5, 6), second.Select(p => (int)p.Value));
    }

    [Fact]
    public void AddMetric_EvictionLeavesASequenceGap_ThatTheReaderCanDetect()
    {
        // 驱逐先于上报时，点从队列消失但序号不回退 —— 缺口就是"没报出去就没了"的量
        var store = new TelemetryStore(maxPointsPerMetric: 10);

        for (var i = 1; i <= 25; i++)
        {
            store.AddMetric("test.metric", Point(i));
        }

        var page = store.ReadMetricPointsAfter("test.metric", afterSequence: 0, maxCount: 2000);

        Assert.Equal(10, page.Count);
        Assert.Equal(16, page[0].Sequence);
        Assert.Equal(15, page[0].Sequence - 0 - 1);
    }

    [Fact]
    public void AddAccessLog_BeyondCap_CountsEveryDiscardedEntry()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 50000, maxAccessLogs: 5);

        for (var i = 0; i < 12; i++)
        {
            store.AddAccessLog(new AccessLogEntry { Path = $"/p{i}" });
        }

        Assert.Equal(5, store.AccessLogs.Count);
        Assert.Equal(7, store.DroppedAccessLogs);
        // 留下的是最新的 5 条：丢弃必须是 FIFO
        Assert.Equal("/p7", store.AccessLogs.First().Path);
    }

    [Fact]
    public void AddAccessLog_BelowCap_ReportsNoDrops()
    {
        var store = new TelemetryStore(maxPointsPerMetric: 50000, maxAccessLogs: 100);

        for (var i = 0; i < 20; i++)
        {
            store.AddAccessLog(new AccessLogEntry { Path = $"/p{i}" });
        }

        Assert.Equal(0, store.DroppedAccessLogs);
    }

    [Fact]
    public void DrainAccessLogs_DoesNotCountAsDrops()
    {
        // 取走不是丢失：心跳正常抽取绝不能进丢弃计数
        var store = new TelemetryStore(maxPointsPerMetric: 50000, maxAccessLogs: 10);
        for (var i = 0; i < 10; i++)
        {
            store.AddAccessLog(new AccessLogEntry { Path = $"/p{i}" });
        }

        var drained = store.DrainAccessLogs(4);

        Assert.Equal(4, drained.Count);
        Assert.Equal(0, store.DroppedAccessLogs);
    }

    [Fact]
    public void EvictionSelfObservingPoints_AlsoCarrySequences_SoTheCursorReaderCanSeeThem()
    {
        // RecordEviction 直接入队而不是走 AddMetric 时，点会带着序号 0 进队列，
        // 而游标读取只看序号大于游标的点 —— 那等于驱逐计数永远报不出去，且没有任何报错
        var store = new TelemetryStore(maxPointsPerMetric: 10);

        for (var i = 0; i < 25; i++)
        {
            store.AddMetric("test.metric", Point());
        }

        var dropped = store.ReadMetricPointsAfter(TelemetryStore.DroppedMetricName, afterSequence: 0, maxCount: 100);

        Assert.NotEmpty(dropped);
        Assert.All(dropped, p => Assert.True(p.Sequence > 0));
    }

    [Fact]
    public void SelfObservingCounterNames_AreDistinctFromRequestMetricNames()
    {
        // 三个自观测计数名字不同而不是共用 kind 标签：
        // 求和式聚合按名字区分"每请求点"与"累计值点"，加标签等于改掉已归档的固定空标签契约
        Assert.NotEqual(TelemetryStore.DroppedMetricName, TelemetryStore.UnreportedDroppedMetricName);
        Assert.NotEqual(TelemetryStore.UnreportedDroppedMetricName, TelemetryStore.AccessLogDroppedMetricName);
        Assert.Equal("gateway.telemetry.dropped", TelemetryStore.DroppedMetricName);
        Assert.Equal("gateway.telemetry.unreported.dropped", TelemetryStore.UnreportedDroppedMetricName);
        Assert.Equal("gateway.telemetry.access_log.dropped", TelemetryStore.AccessLogDroppedMetricName);
    }
}
