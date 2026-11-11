using Microsoft.AspNetCore.Http;
using MinGo.Core.Services;
using MinGo.DataPlane.Telemetry;

namespace MinGo.DataPlane.Tests;

/// <summary>
/// 单元验证：GatewayTelemetryMiddleware 的记录完整性、指标语义与标签基数边界
/// 契约见 openspec/changes/fix-request-telemetry-correctness/specs/request-telemetry
/// </summary>
public class GatewayTelemetryMiddlewareTests
{
    private static readonly RequestDelegate Ok200 = ctx =>
    {
        ctx.Response.StatusCode = 200;
        return Task.CompletedTask;
    };

    private static DefaultHttpContext NewContext(string path) => new()
    {
        Request = { Method = "GET", Path = path }
    };

    private static async Task RecordAsync(TelemetryStore store, RequestDelegate next, params string[] paths)
    {
        foreach (var path in paths)
        {
            var mw = new GatewayTelemetryMiddleware(next, store);
            await mw.InvokeAsync(NewContext(path));
        }
    }

    /// <summary>
    /// 逐个请求执行并吞掉异常，用于统计"部分请求失败"的场景
    /// </summary>
    private static async Task RecordIgnoringThrowsAsync(
        TelemetryStore store, RequestDelegate next, params string[] paths)
    {
        foreach (var path in paths)
        {
            var mw = new GatewayTelemetryMiddleware(next, store);
            try
            {
                await mw.InvokeAsync(NewContext(path));
            }
            catch (InvalidOperationException)
            {
                // 该场景关注的是异常请求同样被完整记录，异常本身在此吞掉
            }
        }
    }

    private static List<Dictionary<string, object>> Tags(TelemetryStore store, string metric) =>
        store.Metrics.TryGetValue(metric, out var q)
            ? q.Select(p => p.Tags).ToList()
            : new List<Dictionary<string, object>>();

    private static object? FirstTag(TelemetryStore store, string metric, string key) =>
        Tags(store, metric).First().GetValueOrDefault(key);

    // ========== 记录完整性 ==========

    [Fact]
    public async Task Success_RecordedOnce_InTotalAndAccessLog()
    {
        var store = new TelemetryStore();
        await RecordAsync(store, Ok200, "/api/orders");

        Assert.Single(Tags(store, "gateway.requests.total"));
        Assert.Single(store.AccessLogs);
        Assert.Empty(Tags(store, "gateway.requests.errors"));
    }

    [Fact]
    public async Task Exception_RecordedInTotalErrorsAndAccessLog()
    {
        var store = new TelemetryStore();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RecordAsync(store, _ => throw new InvalidOperationException("backend at 10.0.0.1 is down"), "/api/orders"));

        Assert.Single(Tags(store, "gateway.requests.total"));
        Assert.Single(Tags(store, "gateway.requests.errors"));

        var log = Assert.Single(store.AccessLogs);
        Assert.Equal(500, log.StatusCode);
    }

    [Fact]
    public async Task NRequestsMExceptions_TotalIsN_ErrorsIsM()
    {
        var store = new TelemetryStore();
        var calls = 0;
        async Task Next(HttpContext ctx)
        {
            calls++;
            // 6 个请求中每第 3 个抛异常 => 2 次异常
            if (calls % 3 == 0)
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            }
            ctx.Response.StatusCode = 200;
        }

        await RecordIgnoringThrowsAsync(store, Next, "/a", "/b", "/c", "/d", "/e", "/f");

        Assert.Equal(6, Tags(store, "gateway.requests.total").Count);
        Assert.Equal(2, Tags(store, "gateway.requests.errors").Count);
        Assert.Equal(6, store.AccessLogs.Count);

        // 错误率不超过 100%：分子不可能大于分母
        Assert.True(Tags(store, "gateway.requests.errors").Count <= Tags(store, "gateway.requests.total").Count);
    }

    // ========== errors 语义 ==========

    [Fact]
    public async Task ClientError_NotCountedAsGatewayError()
    {
        var store = new TelemetryStore();
        await RecordAsync(store, ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        }, "/missing");

        Assert.Single(Tags(store, "gateway.requests.total"));
        Assert.Empty(Tags(store, "gateway.requests.errors"));
        Assert.Equal(404, FirstTag(store, "gateway.requests.total", "status"));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(504)]
    public async Task ServerError_CountedAsGatewayError(int statusCode)
    {
        var store = new TelemetryStore();
        await RecordAsync(store, ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }, "/upstream/fails");

        Assert.Single(Tags(store, "gateway.requests.errors"));
    }

    // ========== 标签基数 ==========

    [Fact]
    public async Task UnmatchedRequests_SingleRouteSeries_AccessLogKeepsFullPath()
    {
        var store = new TelemetryStore();
        var paths = new[] { "/scan/aaa", "/scan/bbb", "/.env" };
        await RecordAsync(store, Ok200, paths);

        var routeValues = Tags(store, "gateway.requests.total")
            .Select(t => t["route"])
            .Distinct()
            .ToList();

        Assert.Equal(new List<object> { GatewayTelemetryMiddleware.UnmatchedRoute }, routeValues);

        // 排查所需的路径信息没有丢失
        Assert.Equal(paths, store.AccessLogs.Select(l => l.Path));
    }

    [Fact]
    public async Task ExceptionTag_UsesTypeName_NotMessage()
    {
        var store = new TelemetryStore();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RecordAsync(store, _ => throw new InvalidOperationException("secret-token=abc123"), "/api/orders"));

        Assert.Equal("InvalidOperationException", FirstTag(store, "gateway.requests.errors", "error"));

        var flattened = string.Join("|",
            Tags(store, "gateway.requests.errors").SelectMany(t => t.Select(kv => $"{kv.Key}={kv.Value}")));
        Assert.DoesNotContain("secret-token", flattened);
    }

    [Fact]
    public async Task NonProxyRequest_UsesNoneSentinel_ForMissingDimensions()
    {
        var store = new TelemetryStore();
        await RecordAsync(store, Ok200, "/api/orders");

        // 无 IReverseProxyFeature 时写哨兵而不是 null，避免下游分组得到 unknown
        Assert.Equal(GatewayTelemetryMiddleware.NoDimension, FirstTag(store, "gateway.requests.total", "cluster"));
        Assert.Equal(GatewayTelemetryMiddleware.NoDimension, FirstTag(store, "gateway.requests.total", "destination"));
    }

    [Fact]
    public async Task AccessLog_CarriesTheSameProxyDimensionsAsTheMetricTags()
    {
        // 上报链路把 cluster/destination 当独立字段传输（proto 字段 10/11）。
        // 只要它们与标签不是同一处派生，看板就会出现"指标说 A 集群、日志说 B 集群"
        var store = new TelemetryStore();
        await RecordAsync(store, Ok200, "/api/orders");

        var log = store.AccessLogs.Single();
        Assert.Equal(FirstTag(store, "gateway.requests.total", "cluster"), log.Cluster);
        Assert.Equal(FirstTag(store, "gateway.requests.total", "destination"), log.Destination);
    }

    [Fact]
    public async Task LifetimeCounters_MonotonicallyIncreaseWithRecordedRequests()
    {
        // 心跳的 TotalRequests 取这里而不是保留窗口求和：窗口滚动不能让它往回走。
        // 静态计数在同进程多宿主下会累加，因此只能断言"这次请求确实让它 +1"
        var store = new TelemetryStore();
        var before = GatewayMetrics.LifetimeRequestsTotal;

        await RecordAsync(store, Ok200, "/api/orders");

        Assert.Equal(before + 1, GatewayMetrics.LifetimeRequestsTotal);
    }

    // ========== 日志与链路关联 ==========

    [Fact]
    public async Task AccessLog_CarriesTraceKey_EvenWithoutOtelListener()
    {
        // 本测试类不装配 OTel，StartActivity 在无 listener 时返回 null，
        // 这正是要守住的下限：本地开发/未启用 OTLP 时访问日志仍必须能定位单次请求
        var store = new TelemetryStore();
        await RecordAsync(store, Ok200, "/api/orders", "/api/orders");

        var logs = store.AccessLogs.ToList();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.False(string.IsNullOrWhiteSpace(l.TraceId)));

        // 回退键来自 HttpContext.TraceIdentifier，每请求唯一
        Assert.Equal(2, logs.Select(l => l.TraceId).Distinct().Count());

        // SpanId 只在真有 Activity 时写入，回退的 TraceIdentifier 不是 W3C span id，不伪造
        Assert.All(logs, l => Assert.Null(l.SpanId));
    }

    // ========== 出口名称契约 ==========

    /// <summary>
    /// store 指标名与 OTel instrument 名同源，而控制面 MonitoringService 与心跳按字面量读取。
    /// 这里锁定这些字面量：改名不会编译报错，只会让下游静默读到空数据
    /// </summary>
    [Theory]
    [InlineData("gateway.requests.total", nameof(GatewayMetrics.RequestsTotalName))]
    [InlineData("gateway.requests.errors", nameof(GatewayMetrics.RequestsErrorsName))]
    [InlineData("gateway.request.duration", nameof(GatewayMetrics.RequestDurationName))]
    public void MetricNames_MatchWhatControlPlaneReads(string expected, string constantName)
    {
        var value = (string?)typeof(GatewayMetrics)
            .GetField(constantName)?.GetRawConstantValue();

        Assert.Equal(expected, value);
    }

    [Fact]
    public void MeterName_IsGateway_MatchingTheOtelSubscription()
    {
        // AddMeter 订阅的名字与创建 instrument 的 Meter 必须一致，否则 OTLP 侧永远是空壳
        Assert.Equal("Gateway", GatewayMetrics.MeterName);
        Assert.Equal("Gateway", GatewayOpenTelemetry.GatewaySourceName);
    }

    // ========== 健康检查隔离 ==========

    [Fact]
    public async Task HealthCheck_NotRecorded()
    {
        var store = new TelemetryStore();
        await RecordAsync(store, Ok200, "/healthz/live", "/healthz/ready");

        Assert.Empty(store.Metrics);
        Assert.Empty(store.AccessLogs);
    }
}
