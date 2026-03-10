using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Services;

namespace MinGo.ReverseProxy.Controllers
{
    /// <summary>
    /// 遥测数据控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class TelemetryController : ControllerBase
    {
        private readonly TelemetryStore _store;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="store">遥测存储</param>
        public TelemetryController(TelemetryStore store)
        {
            _store = store;
        }

        /// <summary>
        /// 获取最近的跟踪数据
        /// </summary>
        /// <returns>跟踪数据列表</returns>
        [HttpGet("traces")]
        public IActionResult GetTraces()
        {
            var traces = _store.Traces.ToList();
            return Ok(traces.Select(t => new
            {
                Name = t?.DisplayName,
                Duration = t?.GetTagValue("duration"),
                Status = t?.GetTagValue("status"),
                Route = t?.GetTagValue("route"),
                StartTime = t?.StartTimeUtc
            }));
        }

        /// <summary>
        /// 获取指标数据
        /// </summary>
        /// <returns>指标数据</returns>
        [HttpGet("metrics")]
        public IActionResult GetMetrics()
        {
            return Ok(_store.Metrics);
        }

        /// <summary>
        /// 获取统计数据
        /// </summary>
        /// <returns>统计数据</returns>
        [HttpGet("stats")]
        public IActionResult GetStats()
        {
            var totalRequests = _store.Metrics.TryGetValue("gateway.requests.total", out var requestPoints) 
                ? requestPoints.Count 
                : 0;

            var errorRequests = _store.Metrics.TryGetValue("gateway.requests.errors", out var errorPoints) 
                ? errorPoints.Count 
                : 0;

            var durationPoints = _store.Metrics.TryGetValue("gateway.request.duration", out var durationPointsList) 
                ? durationPointsList 
                : new List<MetricPoint>();

            var avgDuration = durationPoints.Count > 0 
                ? durationPoints.Average(p => p.Value) 
                : 0;

            return Ok(new
            {
                TotalRequests = totalRequests,
                ErrorRequests = errorRequests,
                ErrorRate = totalRequests > 0 ? (double)errorRequests / totalRequests * 100 : 0,
                AverageDuration = avgDuration
            });
        }
    }

    /// <summary>
    /// 扩展方法
    /// </summary>
    public static class ActivityExtensions
    {
        /// <summary>
        /// 获取标签值
        /// </summary>
        /// <param name="activity">活动</param>
        /// <param name="key">标签键</param>
        /// <returns>标签值</returns>
        public static object GetTagValue(this System.Diagnostics.Activity activity, string key)
        {
            if (activity == null) return null;
            return activity.Tags.FirstOrDefault(t => t.Key == key).Value;
        }
    }
}