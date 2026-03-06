using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using System.Threading.Tasks;

namespace MinGo.ReverseProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MonitoringController : ControllerBase
    {
        private readonly IMonitoringService _monitoringService;

        public MonitoringController(IMonitoringService monitoringService)
        {
            _monitoringService = monitoringService;
        }

        /// <summary>
        /// 获取系统指标
        /// </summary>
        [HttpGet("metrics")]
        public async Task<ActionResult<MetricsSummary>> GetMetrics()
        {
            var metrics = await _monitoringService.GetMetricsSummaryAsync();
            return Ok(metrics);
        }

        /// <summary>
        /// 获取健康状态
        /// </summary>
        [HttpGet("health")]
        public async Task<ActionResult<object>> GetHealth()
        {
            var systemMetrics = await _monitoringService.GetSystemMetricsAsync();
            return Ok(new { status = "healthy", metrics = systemMetrics });
        }
    }
}