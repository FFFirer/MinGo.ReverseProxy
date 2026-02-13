using Microsoft.AspNetCore.Mvc;

namespace MinGo.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly ILogger<ConfigController> _logger;

    public ConfigController(ILogger<ConfigController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            Status = "Running",
            Version = "1.0.0",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
