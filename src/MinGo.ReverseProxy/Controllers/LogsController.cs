using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MinGo.ReverseProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogsController : ControllerBase
    {
        private readonly ILogService _logService;

        public LogsController(ILogService logService)
        {
            _logService = logService;
        }

        /// <summary>
        /// 获取系统日志
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ErrorLog>>> GetLogs()
        {
            var query = new LogQuery();
            var logs = await _logService.GetErrorLogsAsync(query);
            return Ok(logs);
        }

        /// <summary>
        /// 获取指定级别的日志
        /// </summary>
        [HttpGet("level/{level}")]
        public async Task<ActionResult<IEnumerable<ErrorLog>>> GetLogsByLevel(string level)
        {
            var query = new LogQuery { Level = level };
            var logs = await _logService.GetErrorLogsAsync(query);
            return Ok(logs);
        }
    }
}