using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/cert-manager")]
[Authorize]
public class CertManagerController : ControllerBase
{
    private readonly ICertManagerSettingsService _settings;
    private readonly ICertManagerClient _client;
    private readonly ILogger<CertManagerController> _logger;

    public CertManagerController(
        ICertManagerSettingsService settings,
        ICertManagerClient client,
        ILogger<CertManagerController> logger)
    {
        _settings = settings;
        _client = client;
        _logger = logger;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<CertManagerSettingsDto>> GetSettings()
    {
        return Ok(await _settings.GetAsync());
    }

    [HttpPut("settings")]
    public async Task<ActionResult<CertManagerSettingsDto>> SaveSettings([FromBody] CertManagerSettingsDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.BaseUrl)
            && !Uri.TryCreate(request.BaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return BadRequest(new { message = "BaseUrl 必须是合法的绝对地址（如 http://host:5042）" });
        }

        var saved = await _settings.SaveAsync(request);
        return Ok(saved);
    }

    [HttpPost("test")]
    public async Task<ActionResult<CertManagerConnectionResult>> TestConnection(CancellationToken ct)
    {
        var result = await _client.TestConnectionAsync(ct);
        if (!result.Ok)
        {
            _logger.LogWarning("CertManager 连通性测试失败: {Message}", result.Message);
        }

        return Ok(result);
    }
}
