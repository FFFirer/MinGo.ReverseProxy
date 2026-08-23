using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using MinGo.Core.Services;

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

        try
        {
            await _next(ctx);
            sw.Stop();

            var statusCode = ctx.Response.StatusCode;
            var duration = sw.Elapsed.TotalMilliseconds;

            _store.AddMetric("gateway.requests.total", new MetricPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = 1,
                Tags = new Dictionary<string, object> { { "route", route }, { "status", statusCode } }
            });

            _store.AddMetric("gateway.request.duration", new MetricPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = duration,
                Tags = new Dictionary<string, object> { { "route", route }, { "status", statusCode } }
            });

            if (statusCode >= 400)
            {
                _store.AddMetric("gateway.requests.errors", new MetricPoint
                {
                    Timestamp = DateTime.UtcNow,
                    Value = 1,
                    Tags = new Dictionary<string, object> { { "route", route }, { "status", statusCode } }
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
                Route = route
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            var duration = sw.Elapsed.TotalMilliseconds;

            _store.AddMetric("gateway.requests.errors", new MetricPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = 1,
                Tags = new Dictionary<string, object> { { "route", route }, { "error", ex.Message } }
            });

            activity?.SetTag("error", ex.Message);
            throw;
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
