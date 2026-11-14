using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using MinGo.Core.Services;
using Yarp.ReverseProxy.Model;

namespace MinGo.DataPlane.Telemetry;

/// <summary>
/// 网关遥测中间件 - 采集请求级别指标
/// </summary>
public class GatewayTelemetryMiddleware
{
    private static readonly ActivitySource Source = new("Gateway");

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

        var endpoint = ctx.GetEndpoint();
        var route = endpoint?.DisplayName ?? $"{ctx.Request.Method} {ctx.Request.Path}";
        using var activity = Source.StartActivity($"gateway.request: {route}", ActivityKind.Server);

        var sw = Stopwatch.StartNew();
        var timing = new ResponseTiming();
        ctx.Response.OnStarting(state =>
        {
            ((ResponseTiming)state!).MarkHeaders(sw.ElapsedMilliseconds);
            return Task.CompletedTask;
        }, timing);
        ctx.Response.Body = new TimingResponseBodyStream(ctx.Response.Body, timing, sw);

        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", ex.Message);
            throw;
        }
        finally
        {
            sw.Stop();

            var statusCode = ctx.Response.StatusCode;
            var duration = sw.Elapsed.TotalMilliseconds;
            var proxyFeature = ctx.Features.Get<IReverseProxyFeature>();
            var clusterId = proxyFeature?.Cluster?.Config.ClusterId ?? "";
            var tags = new Dictionary<string, object>
            {
                { "route", route },
                { "status", statusCode },
                { "cluster", clusterId }
            };

            // 无论成功或异常，每个请求恰好记录一次 total/duration/access log，保证比率分母完整
            _store.IncrementCounters(statusCode);

            _store.AddMetric("gateway.requests.total", new MetricPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = 1,
                Tags = tags
            });

            _store.AddMetric("gateway.request.duration", new MetricPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = duration,
                Tags = tags
            });

            if (statusCode >= 400)
            {
                _store.AddMetric("gateway.requests.errors", new MetricPoint
                {
                    Timestamp = DateTime.UtcNow,
                    Value = 1,
                    Tags = tags
                });
            }

            activity?.SetTag("route", route);
            activity?.SetTag("status", statusCode);
            activity?.SetTag("duration", duration);

            _store.AddAccessLog(new AccessLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Method = ctx.Request.Method,
                Path = ctx.Request.Path.ToString(),
                StatusCode = statusCode,
                DurationMs = (long)duration,
                ClientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "",
                Route = route,
                Host = ctx.Request.Host.Value ?? "",
                Cluster = clusterId,
                TtfbMs = timing.TtfbMs,
                DownloadMs = timing.DownloadMs
            });
        }
    }
}

public static class GatewayTelemetryMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayTelemetry(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GatewayTelemetryMiddleware>();
    }
}
