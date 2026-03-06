using Microsoft.AspNetCore.Mvc;
using MinGo.Application.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class LogsController : ControllerBase
{
    private readonly ILogService _logService;

    public LogsController(ILogService logService)
    {
        _logService = logService;
    }

    [HttpGet("access")]
    public async Task<ActionResult<IEnumerable<AccessLog>>> GetAccessLogs([FromQuery] LogQuery query)
    {
        var logs = await _logService.GetAccessLogsAsync(query);
        return Ok(logs);
    }

    [HttpGet("error")]
    public async Task<ActionResult<IEnumerable<ErrorLog>>> GetErrorLogs([FromQuery] LogQuery query)
    {
        var logs = await _logService.GetErrorLogsAsync(query);
        return Ok(logs);
    }

    [HttpGet("statistics")]
    public async Task<ActionResult<LogStatistics>> GetLogStatistics([FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
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

        return File(stream, "application/octet-stream", $"logs_{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.json");
    }
}