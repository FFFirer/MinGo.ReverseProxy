using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Yarp.ReverseProxy.Model;
using MinGo.Core.Services;

namespace MinGo.DataPlane.Telemetry;

/// <summary>
/// 网关遥测中间件 - 采集请求级别指标
/// 采集契约见 openspec/specs/request-telemetry 与 openspec/specs/request-tracing：
/// 每请求在 total 与访问日志中恰好记录一次（异常路径同样记录），
/// errors 只反映网关侧失败，所有标签取值有界，
/// 同一次记录同时进入内存 store 与 OTel instruments。
/// </summary>
public class GatewayTelemetryMiddleware
{
    /// <summary>未匹配任何路由端点时的 route 标签哨兵值，避免用路径产生无界基数</summary>
    public const string UnmatchedRoute = "unmatched";

    /// <summary>代理维度未知（未走反向代理，或没有目标被选中）时的标签哨兵值</summary>
    public const string NoDimension = "none";

    private static readonly ActivitySource Source = new(GatewayOpenTelemetry.GatewaySourceName);

    private readonly RequestDelegate _next;
    private readonly TelemetryStore _store;

    public GatewayTelemetryMiddleware(RequestDelegate next, TelemetryStore store)
    {
        _next = next;
        _store = store;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // 跳过健康检查路径，避免探测流量污染指标
        if (ctx.Request.Path.StartsWithSegments("/healthz"))
        {
            await _next(ctx);
            return;
        }

        // YARP 命中时 DisplayName 即 RouteId（实测有界）；未命中时归一为哨兵值
        var route = ctx.GetEndpoint()?.DisplayName ?? UnmatchedRoute;
        using var activity = Source.StartActivity($"gateway.request: {route}", ActivityKind.Server);

        var sw = Stopwatch.StartNew();
        Exception? error = null;

        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            Record(ctx, route, activity, sw.Elapsed.TotalMilliseconds, error);
        }
    }

    /// <summary>
    /// 成功与异常路径共用的唯一记录出口，保证 store、instruments、访问日志三份输出不分叉
    /// </summary>
    private void Record(
        HttpContext ctx,
        string route,
        Activity? activity,
        double durationMs,
        Exception? error)
    {
        // 异常向上抛出时状态码尚未由异常处理中间件写入，按网关侧失败计
        var statusCode = error is not null
            ? ctx.Response.StatusCode >= 500 ? ctx.Response.StatusCode : StatusCodes.Status500InternalServerError
            : ctx.Response.StatusCode;

        // 代理维度在 next() 之后由 YARP 转发器写入特征
        var proxyFeature = ctx.Features.Get<IReverseProxyFeature>();
        var cluster = proxyFeature?.Cluster?.Config?.ClusterId ?? NoDimension;
        var destination = proxyFeature?.ProxiedDestination?.DestinationId ?? NoDimension;

        // 单一标签构造点：store 与 instruments 从同一份数据派生，避免两条出口漂移
        var tags = new Dictionary<string, object>(4)
        {
            { "route", route },
            { "cluster", cluster },
            { "destination", destination },
            { "status", statusCode }
        };

        var timestamp = DateTime.UtcNow;

        _store.AddMetric(GatewayMetrics.RequestsTotalName, new MetricPoint
        {
            Timestamp = timestamp,
            Value = 1,
            Tags = tags
        });

        _store.AddMetric(GatewayMetrics.RequestDurationName, new MetricPoint
        {
            Timestamp = timestamp,
            Value = durationMs,
            Tags = tags
        });

        // errors 只统计网关侧失败：5xx 与管道异常。客户端 4xx 通过 total 的 status 标签下钻
        var gatewayError = error is not null || statusCode >= 500;
        // 类型名基数有界；原始消息与堆栈由 Serilog 请求日志承载
        var errorType = error?.GetType().Name;

        if (gatewayError)
        {
            var errorTags = new Dictionary<string, object>(tags);
            if (errorType is not null)
            {
                errorTags["error"] = errorType;
            }

            _store.AddMetric(GatewayMetrics.RequestsErrorsName, new MetricPoint
            {
                Timestamp = timestamp,
                Value = 1,
                Tags = errorTags
            });
        }

        // OTel 出口：此前 AddMeter("Gateway") 订阅的是没有任何 instrument 的空 Meter
        GatewayMetrics.Record(ToTagPairs(tags), durationMs, gatewayError, errorType);

        if (activity is not null)
        {
            activity.SetTag("route", route);
            activity.SetTag("cluster", cluster);
            activity.SetTag("destination", destination);
            activity.SetTag("status", statusCode);
            activity.SetTag("duration", durationMs);
            activity.SetTag("http.request.method", ctx.Request.Method);
            activity.SetTag("url.path", ctx.Request.Path);
            activity.SetTag("http.response.status_code", statusCode);

            if (gatewayError)
            {
                // 与 errors 指标同一判定：管道异常与 5xx 都是网关侧失败，
                // 只标异常会让上游 502 的 span 在链路后端显示为成功
                activity.SetStatus(ActivityStatusCode.Error, errorType ?? statusCode.ToString());
            }
        }

        _store.AddAccessLog(new AccessLogEntry
        {
            Timestamp = timestamp,
            Method = ctx.Request.Method,
            Path = ctx.Request.Path.ToString(),
            StatusCode = statusCode,
            DurationMs = (long)durationMs,
            ClientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "",
            Route = route,
            // 代理维度与标签同源：另起一条派生路径就会让"日志里的集群"和"指标里的集群"漂移
            Cluster = cluster,
            Destination = destination,
            // 关联键：有 Activity 时用 W3C TraceId，否则回退 ASP.NET Core 的 TraceIdentifier，
            // 保证本地未启用 OTLP 时访问日志仍可定位单次请求
            TraceId = activity?.TraceId.ToString() ?? ctx.TraceIdentifier,
            SpanId = activity?.SpanId.ToString()
        });
    }

    /// <summary>
    /// 标签字典派生为 instrument 标签数组。
    /// status 作为 int 装箱会与 store 中的取值形态不一致，统一按字符串传给 instrument。
    /// </summary>
    private static KeyValuePair<string, object?>[] ToTagPairs(Dictionary<string, object> tags)
    {
        var pairs = new KeyValuePair<string, object?>[tags.Count];
        var i = 0;
        foreach (var (key, value) in tags)
        {
            pairs[i++] = new KeyValuePair<string, object?>(key, value);
        }

        return pairs;
    }
}

public static class GatewayTelemetryMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayTelemetry(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GatewayTelemetryMiddleware>();
    }
}
