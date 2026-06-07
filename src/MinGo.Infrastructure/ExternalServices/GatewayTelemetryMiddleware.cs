using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using MinGo.Core.Services;

namespace MinGo.Infrastructure.ExternalServices
{
    /// <summary>
    /// 网关遥测中间件
    /// </summary>
    public class GatewayTelemetryMiddleware
    {
        private static readonly ActivitySource Source = new("Gateway");

        private readonly RequestDelegate _next;
        private readonly TelemetryStore _store;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="next">下一个中间件</param>
        /// <param name="store">遥测存储</param>
        public GatewayTelemetryMiddleware(RequestDelegate next, TelemetryStore store)
        {
            _next = next;
            _store = store;
        }

        /// <summary>
        /// 处理请求
        /// </summary>
        /// <param name="ctx">HTTP上下文</param>
        /// <returns>任务</returns>
        public async Task Invoke(HttpContext ctx)
        {
            var endpoint = ctx.GetEndpoint();
            var route = endpoint?.DisplayName ?? $"{ctx.Request.Method} {ctx.Request.Path}";
            using var activity = Source.StartActivity($"gateway.request: {route}", ActivityKind.Server);

            var sw = Stopwatch.StartNew();

            try
            {
                await _next(ctx);

                sw.Stop();

                // 记录指标
                var statusCode = ctx.Response.StatusCode;
                var duration = sw.Elapsed.TotalMilliseconds;

                // 添加请求总数指标
                _store.AddMetric("gateway.requests.total", new MetricPoint
                {
                    Timestamp = DateTime.UtcNow,
                    Value = 1,
                    Tags = new Dictionary<string, object>
                    {
                        { "route", route },
                        { "status", statusCode }
                    }
                });

                // 添加请求持续时间指标
                _store.AddMetric("gateway.request.duration", new MetricPoint
                {
                    Timestamp = DateTime.UtcNow,
                    Value = duration,
                    Tags = new Dictionary<string, object>
                    {
                        { "route", route },
                        { "status", statusCode }
                    }
                });

                // 添加错误请求指标
                if (statusCode >= 400)
                {
                    _store.AddMetric("gateway.requests.errors", new MetricPoint
                    {
                        Timestamp = DateTime.UtcNow,
                        Value = 1,
                        Tags = new Dictionary<string, object>
                        {
                            { "route", route },
                            { "status", statusCode }
                        }
                    });
                }

                // 记录跟踪数据
                activity?.SetTag("route", route);
                activity?.SetTag("status", statusCode);
                activity?.SetTag("duration", duration);

                // 添加到遥测存储
                if (activity != null)
                    _store.AddTrace(activity);
            }
            catch (Exception ex)
            {
                sw.Stop();

                // 记录错误指标
                var duration = sw.Elapsed.TotalMilliseconds;

                _store.AddMetric("gateway.requests.errors", new MetricPoint
                {
                    Timestamp = DateTime.UtcNow,
                    Value = 1,
                    Tags = new Dictionary<string, object>
                    {
                        { "route", route },
                        { "error", ex.Message }
                    }
                });

                _store.AddMetric("gateway.request.duration", new MetricPoint
                {
                    Timestamp = DateTime.UtcNow,
                    Value = duration,
                    Tags = new Dictionary<string, object>
                    {
                        { "route", route },
                        { "error", ex.Message }
                    }
                });

                // 记录错误跟踪数据
                activity?.SetTag("route", route);
                activity?.SetTag("status", 500);
                activity?.SetTag("duration", duration);
                activity?.SetTag("error", ex.Message);

                // 添加到遥测存储
                if (activity != null)
                    _store.AddTrace(activity);

                throw;
            }
        }
    }

    /// <summary>
    /// 中间件扩展
    /// </summary>
    public static class GatewayTelemetryMiddlewareExtensions
    {
        /// <summary>
        /// 使用网关遥测中间件
        /// </summary>
        /// <param name="app">应用构建器</param>
        /// <returns>应用构建器</returns>
        public static IApplicationBuilder UseGatewayTelemetry(this IApplicationBuilder app)
        {
            return app.UseMiddleware<GatewayTelemetryMiddleware>();
        }
    }
}