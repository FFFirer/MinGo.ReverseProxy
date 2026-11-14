using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using MinGo.Core.Services;

namespace MinGo.DataPlane.Telemetry;

/// <summary>
/// 数据面遥测管道的单点装配。
/// Program.cs 与测试共用同一份配置，避免"生产订阅的 source 名"与"测试验证的 source 名"漂移
/// —— 本变更要修的正是这类静默失效：订阅名字写错不会报错，只会永远没有数据。
/// </summary>
public static class GatewayOpenTelemetry
{
    /// <summary>自研网关 span 的 source 名</summary>
    public const string GatewaySourceName = "Gateway";

    /// <summary>
    /// YARP 的 source 名。运行时实测确认：代理一次请求时
    /// ActivityListener 收到 <c>source=Yarp.ReverseProxy op=proxy.forwarder</c>，
    /// 且该 span 已携带 proxy.route_id / proxy.cluster_id / proxy.destination_id。
    /// </summary>
    public const string YarpSourceName = "Yarp.ReverseProxy";

    /// <summary>
    /// 注册网关 tracing（含本地 trace 落地）与 metrics（含 Gateway instruments）到 OTLP。
    /// </summary>
    public static OpenTelemetryBuilder AddGatewayTelemetry(
        this OpenTelemetryBuilder builder,
        string serviceName,
        Uri otlpEndpoint)
    {
        builder.ConfigureResource(r => r.AddService(serviceName));

        // 本地 trace 落地需要 TelemetryStore。
        // OpenTelemetry 1.15.3 的 WithTracing 没有接受 IServiceProvider 的重载
        // （实测：该版本只有 Action<TracerProviderBuilder> 与无参两种），
        // 改用 AddProcessor(Func<IServiceProvider, BaseProcessor<Activity>>) 在容器内解析依赖。
        builder.WithTracing(tracing => tracing
            .AddSource(GatewaySourceName, YarpSourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddProcessor(sp => new SimpleActivityExportProcessor(
                new LocalTraceExporter(sp.GetRequiredService<TelemetryStore>())))
            .AddOtlpExporter(o => o.Endpoint = otlpEndpoint));

        builder.WithMetrics(metrics => metrics
            .AddMeter(GatewayMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = otlpEndpoint));

        return builder;
    }
}
