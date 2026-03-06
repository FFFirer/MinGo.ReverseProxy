using Microsoft.AspNetCore.Mvc;
using MinGo.Application.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CertificatesController : ControllerBase
{
    private readonly IApiManagementService _apiManagementService;

    public CertificatesController(IApiManagementService apiManagementService)
    {
        _apiManagementService = apiManagementService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CertificateConfig>>> GetCertificates()
    {
        var certificates = await _apiManagementService.GetCertificatesAsync();
        return Ok(certificates);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CertificateConfig>> GetCertificate(string id)
    {
        var certificate = await _apiManagementService.GetCertificateAsync(id);
        if (certificate == null)
        {
            return NotFound();
        }
        return Ok(certificate);
    }

    [HttpPost]
    public async Task<ActionResult<CertificateConfig>> CreateCertificate(CertificateConfig certificate)
    {
        var createdCertificate = await _apiManagementService.CreateCertificateAsync(certificate);
        return CreatedAtAction(nameof(GetCertificate), new { id = createdCertificate.Id }, createdCertificate);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CertificateConfig>> UpdateCertificate(string id, CertificateConfig certificate)
    {
        var updatedCertificate = await _apiManagementService.UpdateCertificateAsync(id, certificate);
        if (updatedCertificate == null)
        {
            return NotFound();
        }
        return Ok(updatedCertificate);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCertificate(string id)
    {
        await _apiManagementService.DeleteCertificateAsync(id);
        return NoContent();
    }
}