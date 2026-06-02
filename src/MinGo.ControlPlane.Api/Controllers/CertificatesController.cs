using Microsoft.AspNetCore.Mvc;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CertificatesController : ControllerBase
{
    private readonly IApiManagementService _apiManagementService;
    private readonly ILogger<CertificatesController> _logger;

    public CertificatesController(IApiManagementService apiManagementService, ILogger<CertificatesController> logger)
    {
        _apiManagementService = apiManagementService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CertificateConfig>>> GetCertificates()
    {
        var certs = await _apiManagementService.GetCertificatesAsync();
        return Ok(certs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CertificateConfig>> GetCertificate(string id)
    {
        var cert = await _apiManagementService.GetCertificateAsync(id);
        if (cert == null) return NotFound();
        return Ok(cert);
    }

    [HttpPost]
    public async Task<ActionResult<CertificateConfig>> CreateCertificate([FromBody] CertificateConfig certificate)
    {
        var created = await _apiManagementService.CreateCertificateAsync(certificate);
        return CreatedAtAction(nameof(GetCertificate), new { id = created.Id }, created);
    }

    [HttpPost("upload")]
    public async Task<ActionResult<CertificateConfig>> UploadCertificate(
        IFormFile certificateFile,
        [FromForm] string domainName,
        [FromForm] string? password = null)
    {
        if (certificateFile == null || certificateFile.Length == 0)
            return BadRequest("请选择证书文件");

        using var memoryStream = new MemoryStream();
        await certificateFile.CopyToAsync(memoryStream);
        var certificateData = memoryStream.ToArray();

        CertificateConfig certificateConfig;
        try
        {
            certificateConfig = ParseCertificate(certificateData, password, domainName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析证书失败");
            return BadRequest($"证书解析失败: {ex.Message}");
        }

        var created = await _apiManagementService.CreateCertificateAsync(certificateConfig);
        return CreatedAtAction(nameof(GetCertificate), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CertificateConfig>> UpdateCertificate(string id, [FromBody] CertificateConfig certificate)
    {
        var updated = await _apiManagementService.UpdateCertificateAsync(id, certificate);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCertificate(string id)
    {
        await _apiManagementService.DeleteCertificateAsync(id);
        return NoContent();
    }

    private static CertificateConfig ParseCertificate(byte[] certificateData, string? password, string domainName)
    {
        var certificate = LoadCertificate(certificateData, password);
        return new CertificateConfig
        {
            Id = Guid.NewGuid().ToString(),
            DomainName = domainName,
            CertificateType = certificate.HasPrivateKey ? "Pfx" : "Cer",
            CreatedAt = DateTimeOffset.Now,
            ExpiresAt = certificate.NotAfter,
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            Thumbprint = certificate.Thumbprint,
            IsValid = DateTime.Now >= certificate.NotBefore && DateTime.Now <= certificate.NotAfter,
            CertificateData = certificateData,
            Password = password
        };
    }

    private static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadCertificate(
        byte[] certificateData, string? password)
    {
        if (!string.IsNullOrEmpty(password))
            return System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
                certificateData, password,
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);

        try
        {
            return System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
                certificateData, null,
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);
        }
        catch
        {
            return System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(certificateData);
        }
    }
}
