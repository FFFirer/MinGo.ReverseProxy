using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MinGo.ControlPlane.Api.Controllers;
using MinGo.Core.Services;

namespace MinGo.ControlPlane.Api.Tests;

/// <summary>
/// 控制面读取侧的口径测试。
/// 阶段三之前这里数点的个数，阶段二之后上报是抽样与顺延的 ——
/// 个数与值不再相等，唯一正确的计数口径是对点值求和。
/// </summary>
public class TelemetryCaliberTests
{
    [Fact]
    public void GetStats_CountsPointsByValue_NotByNumberOfPoints()
    {
        var store = new TelemetryStore();

        // 三点各代表 5 个请求：抽样上报后"点的个数"和"请求数"必然分叉
        for (var i = 0; i < 3; i++)
        {
            store.AddMetric(GatewayMetricNames.RequestsTotal, Point(5));
        }

        store.AddMetric(GatewayMetricNames.RequestsErrors, Point(1));

        var stats = Ok(GetController(store).GetStats(null));

        Assert.Equal(15.0, Read(stats, "TotalRequests"));
        Assert.Equal(1.0, Read(stats, "ErrorRequests"));

        // 若这里退回 Count 口径，TotalRequests 会是 3 —— 少报 8 倍
        Assert.NotEqual(3.0, Read(stats, "TotalRequests"));
        Assert.Equal(1d / 15d * 100d, Read(stats, "ErrorRate"), 10);
    }

    [Fact]
    public void GetStats_AveragesDurationOverPointValues()
    {
        var store = new TelemetryStore();
        store.AddMetric(GatewayMetricNames.RequestDuration, Point(10));
        store.AddMetric(GatewayMetricNames.RequestDuration, Point(30));

        var stats = Ok(GetController(store).GetStats(null));

        Assert.Equal(20.0, Read(stats, "AverageDuration"));
    }

    [Fact]
    public void GetStats_IsEmptyNotDivideByZero_WhenNoPointsArrived()
    {
        var store = new TelemetryStore();

        var stats = Ok(GetController(store).GetStats(null));

        Assert.Equal(0.0, Read(stats, "TotalRequests"));
        Assert.Equal(0.0, Read(stats, "ErrorRate"));
        Assert.Equal(0.0, Read(stats, "AverageDuration"));
    }

    [Fact]
    public void GetStats_And_GetMetrics_FilterOnTheInstanceIdThatIngestStamped()
    {
        var store = new TelemetryStore();
        store.AddMetric(GatewayMetricNames.RequestsTotal, Point(4, "dp-a"));
        store.AddMetric(GatewayMetricNames.RequestsTotal, Point(7, "dp-a"));
        store.AddMetric(GatewayMetricNames.RequestsTotal, Point(100, "dp-b"));

        var controller = GetController(store);

        Assert.Equal(11.0, Read(Ok(controller.GetStats("dp-a")), "TotalRequests"));
        Assert.Equal(100.0, Read(Ok(controller.GetStats("dp-b")), "TotalRequests"));
        Assert.Equal(111.0, Read(Ok(controller.GetStats(null)), "TotalRequests"));

        // instance_id 标签此前只写不读；现在它同时约束指标明细出口
        Assert.Equal(2, PointCount(Ok(controller.GetMetrics("dp-a")), GatewayMetricNames.RequestsTotal));
        Assert.Equal(1, PointCount(Ok(controller.GetMetrics("dp-b")), GatewayMetricNames.RequestsTotal));
        Assert.Equal(3, PointCount(Ok(controller.GetMetrics(null)), GatewayMetricNames.RequestsTotal));
    }

    [Fact]
    public void GetTagValue_ReadsStronglyTypedTagValues()
    {
        using var listener = TraceListener();
        var source = new ActivitySource(nameof(TelemetryCaliberTests));

        var activity = source.CreateActivity("gateway.request", ActivityKind.Server);
        Assert.NotNull(activity);
        activity!.Start();
        activity.SetTag("status", 500);
        activity.SetTag("duration", 12.34);
        activity.SetTag("route", "orders");

        // .NET 10 的 Activity.Tags 只投影字符串形态的值，int/double 在那里读不出来。
        // 这段断言是"为什么必须用 EnumerateTagObjects"的证据，不是实现细节的复述
        Assert.Equal(500, activity.GetTagValue("status"));
        Assert.Equal(12.34, activity.GetTagValue("duration"));
        Assert.Equal("orders", activity.GetTagValue("route"));
        Assert.Null(activity.GetTagValue("missing"));
        Assert.Null(((Activity?)null).GetTagValue("status"));
    }

    [Fact]
    public void GetTraces_ExposesNumericTagValues_ThroughTheSameReader()
    {
        using var listener = TraceListener();
        var source = new ActivitySource(nameof(TelemetryCaliberTests) + ".traces");

        var store = new TelemetryStore();
        using (var activity = source.CreateActivity("gateway.request", ActivityKind.Server))
        {
            Assert.NotNull(activity);
            activity!.SetTag("status", 502);
            activity.SetTag("duration", 7.5);
            activity.SetTag("route", "payments");
            activity.Start();
            activity.Stop();
            store.AddTrace(activity);
        }

        var traces = Ok(GetController(store).GetTraces());
        Assert.Equal(1, traces.GetArrayLength());

        var trace = traces[0];
        Assert.Equal(502, trace.GetProperty("Status").GetInt32());
        Assert.Equal(7.5, trace.GetProperty("Duration").GetDouble());
        Assert.Equal("payments", trace.GetProperty("Route").GetString());
    }

    private static TelemetryController GetController(TelemetryStore store) => new(store);

    private static MetricPoint Point(double value, string? instanceId = null)
    {
        var tags = new Dictionary<string, object>();
        if (instanceId is not null)
        {
            tags["instance_id"] = instanceId;
        }

        return new MetricPoint
        {
            // 保留期外的点在写入时就被清掉，测试点必须落在保留期内
            Timestamp = DateTime.UtcNow,
            Value = value,
            Tags = tags
        };
    }

    private static JsonElement Ok(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value);
    }

    private static double Read(JsonElement payload, string name) =>
        payload.GetProperty(name).GetDouble();

    private static int PointCount(JsonElement payload, string metricName) =>
        payload.GetProperty(metricName).GetArrayLength();

    private static ActivityListener TraceListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
