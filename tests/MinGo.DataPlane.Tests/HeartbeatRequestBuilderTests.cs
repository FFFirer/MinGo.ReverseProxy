using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MinGo.Core.Services;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Grpc;
using MinGo.DataPlane.Heartbeat;
using MinGo.DataPlane.Telemetry;
using Moq;
using CoreMetricPoint = MinGo.Core.Services.MetricPoint;
using CoreAccessLogEntry = MinGo.Core.Services.AccessLogEntry;
using GrpcMetricPoint = MinGo.DataPlane.Grpc.MetricPoint;
using YarpRouteConfig = Yarp.ReverseProxy.Configuration.RouteConfig;
using YarpClusterConfig = Yarp.ReverseProxy.Configuration.ClusterConfig;

namespace MinGo.DataPlane.Tests;

/// <summary>
/// 心跳上报的行为测试 —— 本仓库第一次覆盖这段代码。
/// 契约见 telemetry-reporting 规范。这里锁死的是三类曾经静默失效的东西：
/// 上报覆盖是否完整、游标推进时机、以及标量到底是不是真数据。
/// </summary>
public class HeartbeatRequestBuilderTests
{
    private readonly TelemetryStore _store = new(maxPointsPerMetric: 50000, maxAccessLogs: 5000);
    private readonly FakeProcessSampler _sampler = new();
    private readonly DataPlaneConfigProvider _configProvider;
    private (long Total, long Errors) _lifetimeTotals;

    public HeartbeatRequestBuilderTests()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton("DataPlane", new Yarp.ReverseProxy.Configuration.InMemoryConfigProvider(
            Array.Empty<YarpRouteConfig>(), Array.Empty<YarpClusterConfig>()));
        _configProvider = new DataPlaneConfigProvider(
            services.BuildServiceProvider(),
            new Mock<ILogger<MinGo.DataPlane.ConfigSync.DataPlaneConfigProvider>>().Object);
    }

    private HeartbeatRequestBuilder NewBuilder(int maxPointsPerName = 2000, int maxAccessLogs = 200) => new(
        _store,
        new GatewayIdentity(),
        _sampler,
        _configProvider,
        () => _lifetimeTotals,
        maxPointsPerName,
        maxAccessLogs,
        NullLogger<HeartbeatRequestBuilder>.Instance);

    private void RecordRequests(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _store.AddMetric(GatewayMetrics.RequestsTotalName, new CoreMetricPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = 1,
                Tags = new Dictionary<string, object> { ["route"] = "r1" }
            });
        }
    }

    private static IEnumerable<GrpcMetricPoint> For(HeartbeatRequest request, string name) =>
        request.Metrics.Where(m => m.Name == name);

    [Fact]
    public void Build_WhenMorePointsThanTheOldTopTen_AllOfThemAreReported()
    {
        // 原实现每周期只发最后 10 个点：37 个请求在控制面会变成 10 个
        RecordRequests(37);

        var request = NewBuilder().Build();

        Assert.Equal(37, For(request, GatewayMetrics.RequestsTotalName).Count());
    }

    [Fact]
    public void Build_AfterCommit_DoesNotRepeatAlreadyReportedPoints()
    {
        RecordRequests(10);
        var builder = NewBuilder();

        var first = builder.Build();
        builder.Commit();
        RecordRequests(5);
        var second = builder.Build();

        Assert.Equal(10, For(first, GatewayMetrics.RequestsTotalName).Count());
        Assert.Equal(5, For(second, GatewayMetrics.RequestsTotalName).Count());
    }

    [Fact]
    public void Build_WithoutCommit_KeepsTheSamePagePending()
    {
        // 写流失败时不提交：数据必须还在，否则"报不出去"就变成了"没发生过"
        RecordRequests(12);
        var builder = NewBuilder();

        var first = builder.Build();
        var replay = builder.Build();

        Assert.Equal(12, For(first, GatewayMetrics.RequestsTotalName).Count());
        Assert.Equal(12, For(replay, GatewayMetrics.RequestsTotalName).Count());
    }

    [Fact]
    public void Build_WhenPerCycleCapIsHit_DefersTheRestWithoutCountingItLost()
    {
        RecordRequests(12);
        var builder = NewBuilder(maxPointsPerName: 5);

        var first = builder.Build();
        builder.Commit();
        var second = builder.Build();

        Assert.Equal(5, For(first, GatewayMetrics.RequestsTotalName).Count());
        Assert.Equal(5, For(second, GatewayMetrics.RequestsTotalName).Count());

        // 顺延不是丢失：未上报丢弃计数必须保持 0
        var unreported = For(second, TelemetryStore.UnreportedDroppedMetricName).ToList();
        Assert.NotEmpty(unreported);
        Assert.All(unreported, point => Assert.Equal(0d, point.Value));
    }

    [Fact]
    public void Build_WhenPointsWereEvictedBeforeReporting_CountsThemAsUnreported()
    {
        // 上限 10 点：前 15 个点在报出去之前就被本地驱逐
        var bounded = new TelemetryStore(maxPointsPerMetric: 10, maxAccessLogs: 5000);
        var builder = new HeartbeatRequestBuilder(
            bounded,
            new GatewayIdentity(),
            _sampler,
            _configProvider,
            () => _lifetimeTotals,
            2000,
            200,
            NullLogger<HeartbeatRequestBuilder>.Instance);

        for (var i = 0; i < 25; i++)
        {
            bounded.AddMetric(GatewayMetrics.RequestsTotalName, new CoreMetricPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = 1
            });
        }

        var request = builder.Build();

        Assert.Equal(10, For(request, GatewayMetrics.RequestsTotalName).Count());
        var unreported = For(request, TelemetryStore.UnreportedDroppedMetricName).Single().Value;
        Assert.Equal(15d, unreported);
        // 本地容量驱逐同样要出去，否则"存不下"和"没上报"混成一团
        Assert.NotEmpty(For(request, TelemetryStore.DroppedMetricName));
    }

    [Fact]
    public void Build_ReportsSelfObservedCountersEveryCycle()
    {
        var builder = NewBuilder();

        var first = builder.Build();
        builder.Commit();
        var second = builder.Build();

        // 每拍都报累计值：控制面要能看到这条曲线是平的还是在涨
        Assert.Single(For(first, TelemetryStore.AccessLogDroppedMetricName));
        Assert.Single(For(second, TelemetryStore.AccessLogDroppedMetricName));
        Assert.All(second.Metrics, point =>
        {
            if (point.Name.StartsWith("gateway.telemetry.", StringComparison.Ordinal))
            {
                Assert.Empty(point.Tags);
            }
        });
    }

    [Fact]
    public void Build_TakesCpuAndMemoryFromTheSampler()
    {
        _sampler.Usage = new ProcessUsage(CpuPercent: 42.5, MemoryMegabytes: 128);

        var request = NewBuilder().Build();

        Assert.Equal(42.5, request.CpuUsage);
        Assert.Equal(128, request.MemoryUsage);
    }

    [Fact]
    public void Build_TakesRequestTotalsFromLifetimeCounters()
    {
        _lifetimeTotals = (Total: 1_000_000, Errors: 7);

        var request = NewBuilder().Build();

        Assert.Equal(1_000_000, request.TotalRequests);
        Assert.Equal(7, request.ErrorRequests);
    }

    [Fact]
    public void Build_WithoutAppliedConfig_ReportsUnhealthy()
    {
        var request = NewBuilder().Build();

        Assert.False(request.IsHealthy);
        Assert.False(_configProvider.LastApplySucceeded);
    }

    [Fact]
    public void Build_AfterConfigApplied_ReportsHealthy()
    {
        _configProvider.ApplyConfig(new ConfigSnapshot
        {
            Version = 1,
            UpdateType = UpdateType.FullSync,
            Routes = { new RouteConfig { Id = "r1", ClusterId = "c1", MatchPath = "/api/{**catchall}", Enabled = true } },
            Clusters = { new ClusterConfig { Id = "c1", LoadBalancingPolicy = "RoundRobin" } }
        });

        var request = NewBuilder().Build();

        Assert.True(_configProvider.LastApplySucceeded);
        Assert.True(request.IsHealthy);
    }

    [Fact]
    public void Build_MapsCorrelationKeysAndProxyDimensionsOntoAccessLogs()
    {
        _store.AddAccessLog(new CoreAccessLogEntry
        {
            Timestamp = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc),
            Method = "GET",
            Path = "/api/orders",
            StatusCode = 502,
            DurationMs = 15,
            ClientIp = "10.0.0.1",
            Route = "r1",
            Cluster = "backend",
            Destination = "d1",
            TraceId = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
            SpanId = "00f067aa0ba902b7"
        });

        var log = NewBuilder().Build().AccessLogs.Single();

        Assert.Equal("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00", log.TraceId);
        Assert.Equal("00f067aa0ba902b7", log.SpanId);
        Assert.Equal("backend", log.Cluster);
        Assert.Equal("d1", log.Destination);
    }

    [Fact]
    public void Build_WhenDimensionsWereNeverCollected_SendsEmptyRatherThanNone()
    {
        // none 是采集侧"量过且不存在"的哨兵；没采集就是空串，接收方才能区分两者
        _store.AddAccessLog(new CoreAccessLogEntry { Path = "/x", Route = GatewayTelemetryMiddleware.NoDimension });

        var log = NewBuilder().Build().AccessLogs.Single();

        Assert.Equal("", log.TraceId);
        Assert.Equal("", log.SpanId);
        Assert.Equal("", log.Cluster);
        Assert.Equal("", log.Destination);
        Assert.Equal(GatewayTelemetryMiddleware.NoDimension, log.Route);
    }

    [Fact]
    public void Build_AccessLogPageIsBoundedAndPendingEntriesAreReplayedInOrder()
    {
        for (var i = 0; i < 5; i++)
        {
            _store.AddAccessLog(new CoreAccessLogEntry { Path = $"/p{i}" });
        }

        var builder = NewBuilder(maxAccessLogs: 2);

        var first = builder.Build();
        Assert.Equal(new[] { "/p0", "/p1" }, first.AccessLogs.Select(l => l.Path));

        // 没提交就不许再取新的：已 drain 的条目必须留在待发队列里，顺序不变
        var retry = builder.Build();
        Assert.Equal(new[] { "/p0", "/p1" }, retry.AccessLogs.Select(l => l.Path));

        builder.Commit();
        var second = builder.Build();
        Assert.Equal(new[] { "/p2", "/p3" }, second.AccessLogs.Select(l => l.Path));

        builder.Commit();
        var third = builder.Build();
        Assert.Equal(new[] { "/p4" }, third.AccessLogs.Select(l => l.Path));
    }

    [Fact]
    public void Build_DrainedAccessLogsDoNotLeakIntoLaterCycles()
    {
        _store.AddAccessLog(new CoreAccessLogEntry { Path = "/only" });
        var builder = NewBuilder();

        builder.Build();
        builder.Commit();
        var afterCommit = builder.Build();

        // drain 已经把它们移出 store，提交后再出现就是重复上报
        Assert.Empty(afterCommit.AccessLogs);
        Assert.Equal(0, builder.PendingAccessLogCount);
    }

    private sealed class FakeProcessSampler : IProcessSampler
    {
        public ProcessUsage Usage { get; set; } = new(0, 0);

        public ProcessUsage Sample() => Usage;
    }
}
