using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinGo.Application.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/cert-enrollments")]
[Authorize]
public class CertEnrollmentsController : ControllerBase
{
    private readonly CertificateEnrollmentService _service;
    private readonly IApiManagementService _apiManagement;
    private readonly ILogger<CertEnrollmentsController> _logger;

    public CertEnrollmentsController(
        CertificateEnrollmentService service,
        IApiManagementService apiManagement,
        ILogger<CertEnrollmentsController> logger)
    {
        _service = service;
        _apiManagement = apiManagement;
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

    /// <summary>移除登记；<paramref name="deleteCertificates"/> 为 true 时级联删除其导入的证书（批量删除、单次广播）。</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] bool deleteCertificates = false)
    {
        var enrollment = (await _service.ListAsync()).FirstOrDefault(e => e.Id == id);
        if (enrollment is null)
        {
            return NotFound(new { message = "登记不存在" });
        }

        if (deleteCertificates)
        {
            var deleted = await _apiManagement.DeleteCertificatesByEnrollmentAsync(id);
            _logger.LogInformation(
                "移除登记 {Domain} 并级联删除 {Count} 张导入证书", enrollment.Domain, deleted);
        }

        await _service.DeleteAsync(id);
        return NoContent();
    }
}
