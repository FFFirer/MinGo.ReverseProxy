using System.Diagnostics.Metrics;
using System.Threading;
using MinGo.Core.Services;

namespace MinGo.DataPlane.Telemetry;

/// <summary>
/// 网关 OTel instruments。
/// Program.cs 的 AddMeter("Gateway") 只有在这里真正创建 instrument 之后才会产出网关指标 ——
/// 在此之前该订阅是空壳，OTLP 后端只能看到框架插针指标。
/// instruments 为静态：与中间件签名解耦，测试宿主无需注册额外服务。
/// </summary>
public static class GatewayMetrics
{
    /// <summary>与 Program.cs 中 AddMeter 的名称必须一致</summary>
    public const string MeterName = "Gateway";

    /// <summary>
    /// instrument 名同时是写入 <c>TelemetryStore</c> 的指标名。
    /// 名字的唯一权威在 <see cref="GatewayMetricNames"/>：控制面查询侧读的是同一份常量，
    /// 改名等于断链，而断链在这里不报错、只是看板变空。
    /// </summary>
    public const string RequestsTotalName = GatewayMetricNames.RequestsTotal;

    /// <summary>errors 指标名，语义为网关侧失败（5xx 与管道异常）</summary>
    public const string RequestsErrorsName = GatewayMetricNames.RequestsErrors;

    /// <summary>耗时指标名</summary>
    public const string RequestDurationName = GatewayMetricNames.RequestDuration;

    private static readonly Meter Instance = new(MeterName, "1.0.0");

    private static readonly Counter<long> RequestsTotal = Instance.CreateCounter<long>(
        RequestsTotalName,
        description: "Total requests passed through the gateway");

    private static readonly Counter<long> RequestsErrors = Instance.CreateCounter<long>(
        RequestsErrorsName,
        description: "Gateway-side failures: 5xx responses and pipeline exceptions");

    private static readonly Histogram<double> RequestDuration = Instance.CreateHistogram<double>(
        RequestDurationName,
        unit: "ms",
        description: "Request duration measured at the gateway edge");

    private static long _lifetimeRequestsTotal;

    private static long _lifetimeRequestsErrors;

    /// <summary>
    /// 进程生命周期内记录的请求总数。
    /// 与保留窗口内的点数求和不同，这个计数不会因为窗口滚动或触顶驱逐而往回走，
    /// 因此心跳上报的累计值语义是"自数据面进程启动以来"。
    /// 放在这里而不是 TelemetryStore：静态类不受 OpenTelemetry:Enabled 影响，
    /// 也避免后人再次把它和保留窗口绑定。
    /// </summary>
    public static long LifetimeRequestsTotal => Interlocked.Read(ref _lifetimeRequestsTotal);

    /// <summary>进程生命周期内记录的网关侧失败总数</summary>
    public static long LifetimeRequestsErrors => Interlocked.Read(ref _lifetimeRequestsErrors);

    /// <summary>
    /// 记录一次请求。标签与写入 TelemetryStore 的点来自同一构造点，避免两条出口漂移。
    /// </summary>
    public static void Record(
        KeyValuePair<string, object?>[] tags,
        double durationMs,
        bool gatewayError,
        string? errorType)
    {
        RequestsTotal.Add(1, tags);
        RequestDuration.Record(durationMs, tags);

        // 单调累计与 instrument 同一构造点：开关关掉只影响 OTel 出口，不影响计数
        Interlocked.Increment(ref _lifetimeRequestsTotal);

        if (gatewayError)
        {
            // errors 需要额外携带 error 类型，仅在失败路径构造第二个标签集
            KeyValuePair<string, object?>[] errorTags = errorType is not null
                ? [.. tags, new KeyValuePair<string, object?>("error", errorType)]
                : tags;

            RequestsErrors.Add(1, errorTags);
            Interlocked.Increment(ref _lifetimeRequestsErrors);
        }
    }
}
