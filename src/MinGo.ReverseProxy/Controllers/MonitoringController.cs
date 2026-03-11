using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MinGo.ReverseProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MonitoringController : ControllerBase
    {
        private readonly IMonitoringService _monitoringService;
        private readonly ILogger<MonitoringController> _logger;

        public MonitoringController(IMonitoringService monitoringService, ILogger<MonitoringController> logger)
        {
            _monitoringService = monitoringService;
            _logger = logger;
        }

        [HttpGet("metrics")]
        public async Task<ActionResult<MetricsSummary>> GetMetricsSummary()
        {
            var summary = await _monitoringService.GetMetricsSummaryAsync();
            return Ok(summary);
        }

        [HttpGet("requests")]
        public async Task<ActionResult<System.Collections.Generic.IEnumerable<RequestMetrics>>> GetRequestMetrics(
            [FromQuery] System.DateTimeOffset start,
            [FromQuery] System.DateTimeOffset end)
        {
            var metrics = await _monitoringService.GetRequestMetricsAsync(start, end);
            return Ok(metrics);
        }

        [HttpGet("services")]
        public async Task<ActionResult<System.Collections.Generic.IEnumerable<ServiceMetrics>>> GetServiceMetrics()
        {
            var metrics = await _monitoringService.GetServiceMetricsAsync();
            return Ok(metrics);
        }

        [HttpGet("errors")]
        public async Task<ActionResult<System.Collections.Generic.IEnumerable<ErrorMetrics>>> GetErrorMetrics(
            [FromQuery] System.DateTimeOffset start,
            [FromQuery] System.DateTimeOffset end)
        {
            var metrics = await _monitoringService.GetErrorMetricsAsync(start, end);
            return Ok(metrics);
        }

        [HttpGet("system")]
        public async Task<ActionResult<SystemMetrics>> GetSystemMetrics()
        {
            var metrics = await _monitoringService.GetSystemMetricsAsync();
            return Ok(metrics);
        }

        [HttpGet("health")]
        public async Task<ActionResult<object>> GetHealth()
        {
            var systemMetrics = await _monitoringService.GetSystemMetricsAsync();
            return Ok(new { status = "healthy", metrics = systemMetrics });
        }
    }
}