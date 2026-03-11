using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Extensions.Logging;

namespace MinGo.ReverseProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogsController : ControllerBase
    {
        private readonly ILogService _logService;
        private readonly ILogger<LogsController> _logger;

        public LogsController(ILogService logService, ILogger<LogsController> logger)
        {
            _logService = logService;
            _logger = logger;
        }

        [HttpGet("access")]
        public async Task<ActionResult<IEnumerable<AccessLog>>> GetAccessLogs(
            [FromQuery] string? routeId = null,
            [FromQuery] string? clusterId = null,
            [FromQuery] int? statusCode = null,
            [FromQuery] int pageSize = 100)
        {
            var query = new LogQuery
            {
                RouteId = routeId,
                ClusterId = clusterId,
                StatusCode = statusCode,
                PageSize = pageSize
            };
            var logs = await _logService.GetAccessLogsAsync(query);
            return Ok(logs);
        }

        [HttpGet("errors")]
        public async Task<ActionResult<IEnumerable<ErrorLog>>> GetErrorLogs(
            [FromQuery] string? level = null,
            [FromQuery] string? routeId = null,
            [FromQuery] int pageSize = 100)
        {
            var query = new LogQuery
            {
                Level = level,
                RouteId = routeId,
                PageSize = pageSize
            };
            var logs = await _logService.GetErrorLogsAsync(query);
            return Ok(logs);
        }

        [HttpGet("statistics")]
        public async Task<ActionResult<LogStatistics>> GetLogStatistics(
            [FromQuery] System.DateTimeOffset start,
            [FromQuery] System.DateTimeOffset end)
        {
            var statistics = await _logService.GetLogStatisticsAsync(start, end);
            return Ok(statistics);
        }

        [HttpPost("export")]
        public async Task<IActionResult> ExportLogs([FromBody] LogQuery query)
        {
            var stream = new MemoryStream();
            await _logService.ExportLogsAsync(query, stream);
            stream.Position = 0;
            return File(stream, "text/csv", "logs.csv");
        }
    }
}