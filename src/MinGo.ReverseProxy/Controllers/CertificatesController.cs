using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using Microsoft.Extensions.Logging;

namespace MinGo.ReverseProxy.Controllers
{
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
            var certificates = await _apiManagementService.GetCertificatesAsync();
            return Ok(certificates);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<CertificateConfig>> GetCertificate(string id)
        {
            var certificate = await _apiManagementService.GetCertificateAsync(id);
            if (certificate == null || string.IsNullOrEmpty(certificate.DomainName))
            {
                return NotFound();
            }
            return Ok(certificate);
        }

        [HttpPost]
        public async Task<ActionResult<CertificateConfig>> CreateCertificate([FromBody] CertificateConfig certificate)
        {
            var created = await _apiManagementService.CreateCertificateAsync(certificate);
            return CreatedAtAction(nameof(GetCertificate), new { id = created.Id }, created);
        }

        [HttpPost("parse-domain")]
        public async Task<ActionResult<string>> ParseCertificateDomain(
            IFormFile certificateFile,
            [FromForm] string? password = null)
        {
            if (certificateFile == null || certificateFile.Length == 0)
            {
                return BadRequest("请选择证书文件");
            }

            using var memoryStream = new MemoryStream();
            await certificateFile.CopyToAsync(memoryStream);
            var certificateData = memoryStream.ToArray();

            try
            {
                var domainName = ExtractDomainFromCertificate(certificateData, password);
                return Ok(domainName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析证书域名失败");
                return BadRequest($"证书解析失败: {ex.Message}");
            }
        }

        [HttpPost("upload")]
        public async Task<ActionResult<CertificateConfig>> UploadCertificate(
            IFormFile certificateFile,
            [FromForm] string domainName,
            [FromForm] string? password = null)
        {
            if (certificateFile == null || certificateFile.Length == 0)
            {
                return BadRequest("请选择证书文件");
            }

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
        public async Task<ActionResult<CertificateConfig>> UpdateCertificate(
            string id,
            [FromBody] CertificateConfig certificate)
        {
            var updated = await _apiManagementService.UpdateCertificateAsync(id, certificate);
            if (updated == null || string.IsNullOrEmpty(updated.DomainName))
            {
                return NotFound();
            }
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCertificate(string id)
        {
            await _apiManagementService.DeleteCertificateAsync(id);
            return NoContent();
        }

        private string ExtractDomainFromCertificate(byte[] certificateData, string? password)
        {
            X509Certificate2 certificate;

            if (!string.IsNullOrEmpty(password))
            {
                certificate = new X509Certificate2(certificateData, password, X509KeyStorageFlags.Exportable);
            }
            else
            {
                try
                {
                    certificate = new X509Certificate2(certificateData, (string?)null, X509KeyStorageFlags.Exportable);
                }
                catch
                {
                    certificate = new X509Certificate2(certificateData);
                }
            }

            var subject = certificate.Subject;
            var cnMatch = System.Text.RegularExpressions.Regex.Match(subject, @"CN=([^,]+)");
            if (cnMatch.Success)
            {
                return cnMatch.Groups[1].Value.Trim();
            }

            return string.Empty;
        }

        private CertificateConfig ParseCertificate(byte[] certificateData, string? password, string domainName)
        {
            X509Certificate2 certificate;

            if (!string.IsNullOrEmpty(password))
            {
                certificate = new X509Certificate2(certificateData, password, X509KeyStorageFlags.Exportable);
            }
            else
            {
                try
                {
                    certificate = new X509Certificate2(certificateData, (string?)null, X509KeyStorageFlags.Exportable);
                }
                catch
                {
                    certificate = new X509Certificate2(certificateData);
                }
            }

            return new CertificateConfig
            {
                Id = System.Guid.NewGuid().ToString(),
                DomainName = domainName,
                CertificateType = certificate.HasPrivateKey ? "Pfx" : "Cer",
                CreatedAt = System.DateTimeOffset.Now,
                ExpiresAt = System.DateTimeOffset.Parse(certificate.NotAfter.ToString("o")),
                Subject = certificate.Subject,
                Issuer = certificate.Issuer,
                Thumbprint = certificate.Thumbprint,
                IsValid = System.DateTime.Now >= certificate.NotBefore && System.DateTime.Now <= certificate.NotAfter,
                CertificateData = certificateData,
                Password = password
            };
        }
    }
}