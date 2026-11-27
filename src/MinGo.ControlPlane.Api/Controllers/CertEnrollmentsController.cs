using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.Application.Services;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/cert-enrollments")]
[Authorize]
public class CertEnrollmentsController : ControllerBase
{
    private readonly CertificateEnrollmentService _service;
    private readonly ILogger<CertEnrollmentsController> _logger;

    public CertEnrollmentsController(
        CertificateEnrollmentService service,
        ILogger<CertEnrollmentsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CertificateEnrollmentDto>>> List()
    {
        return Ok(await _service.ListAsync());
    }

    [HttpPost]
    public async Task<ActionResult<CertificateEnrollmentDto>> Register(
        [FromBody] RegisterCertificateEnrollmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Domain))
        {
            return BadRequest(new { message = "域名不能为空" });
        }

        try
        {
            var enrollment = await _service.RegisterAsync(request, HttpContext.RequestAborted);
            return CreatedAtAction(nameof(List), null, enrollment);
        }
        catch (EnrollmentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/sync")]
    public async Task<IActionResult> Sync(string id)
    {
        var synced = await _service.SyncAsync(id, HttpContext.RequestAborted);
        if (!synced)
        {
            // 同步失败（含无证书可下载）时返回 409，前端据 LastError 展示
            var enrollment = await _service.ListAsync();
            var current = enrollment.FirstOrDefault(e => e.Id == id);
            if (current is null)
            {
                return NotFound(new { message = "登记不存在" });
            }

            return Conflict(new
            {
                message = current.LastError ?? "同步未完成（CertManager 尚无该域名证书）",
                enrollment = current,
            });
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _service.DeleteAsync(id);
        return deleted ? NoContent() : NotFound(new { message = "登记不存在" });
    }
}
