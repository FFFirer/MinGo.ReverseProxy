using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.Application.Services;

/// <summary>
/// 证书自动接入编排：登记（探测→申请）、同步（下载 PFX→幂等导入→广播）、事件处理、对账。
/// 事件与对账共用同一 <see cref="SyncAsync"/>，保证幂等可重入。
/// </summary>
public class CertificateEnrollmentService
{
    private readonly ICertificateEnrollmentStore _store;
    private readonly ICertManagerClient _client;
    private readonly IApiManagementService _apiManagement;
    private readonly ILogger<CertificateEnrollmentService> _logger;

    public CertificateEnrollmentService(
        ICertificateEnrollmentStore store,
        ICertManagerClient client,
        IApiManagementService apiManagement,
        ILogger<CertificateEnrollmentService> logger)
    {
        _store = store;
        _client = client;
        _apiManagement = apiManagement;
        _logger = logger;
    }

    /// <summary>
    /// 登记域名：域名唯一校验 → 探测下载 → 已有证书直接同步；没有才发起申请。
    /// </summary>
    public async Task<CertificateEnrollmentDto> RegisterAsync(
        RegisterCertificateEnrollmentRequest request, CancellationToken ct)
    {
        if (!CertificateEnrollmentPolicy.IsValidDomain(request.Domain))
        {
            throw new EnrollmentException($"域名 '{request.Domain}' 不合法（仅支持裸域名，泛域名请用开关）");
        }

        var normalized = CertificateEnrollmentPolicy.NormalizeDomain(request.Domain);
        if (await _store.GetByDomainAsync(normalized) is not null)
        {
            throw new EnrollmentException($"域名 {normalized} 已登记");
        }

        var enrollment = await _store.CreateAsync(new CertificateEnrollmentDto
        {
            Domain = normalized,
            IsWildcard = request.IsWildcard,
            UseStaging = request.UseStaging,
            State = CertificateEnrollmentState.Unrequested,
        });

        try
        {
            if (await _client.ProbeHasCertificateAsync(normalized, ct))
            {
                // 存量证书直接接入，不触发申请（也避免 LE 限流）
                await SyncAsync(enrollment.Id, ct);
            }
            else
            {
                var externalId = await _client.RequestCertificateAsync(
                    normalized, request.IsWildcard, request.UseStaging, ct);
                enrollment.ExternalCertificateId = externalId;
                enrollment.State = CertificateEnrollmentState.Pending;
                enrollment.LastError = null;
                await _store.UpdateAsync(enrollment.Id, enrollment);
            }
        }
        catch (Exception ex) when (ex is not EnrollmentException)
        {
            _logger.LogError(ex, "证书登记失败: Domain={Domain}", normalized);
            enrollment.State = CertificateEnrollmentState.Failed;
            enrollment.LastError = ex.Message;
            await _store.UpdateAsync(enrollment.Id, enrollment);
        }

        return (await _store.GetByIdAsync(enrollment.Id))!;
    }

    /// <summary>
    /// 同步：下载最新 PFX → 解析 → (DomainName, Thumbprint) 幂等 → 导入新行并触发广播。
    /// 事件消费者与对账工作器共用；任何异常落 LastError 并把状态转 Failed，不向上抛。
    /// </summary>
    public async Task<bool> SyncAsync(string enrollmentId, CancellationToken ct)
    {
        var enrollment = await _store.GetByIdAsync(enrollmentId);
        if (enrollment is null)
        {
            return false;
        }

        if (!enrollment.Enabled)
        {
            return false;
        }

        try
        {
            var password = GeneratePfxPassword();
            var pfx = await _client.DownloadPfxAsync(enrollment.Domain, password, ct);
            if (pfx is null)
            {
                // 无证书可下载：申请中保持 Pending，否则保持 Unrequested，等待事件/对账
                _logger.LogInformation("CertManager 无 {Domain} 的有效证书，跳过同步", enrollment.Domain);
                return false;
            }

            var cert = X509CertificateLoader.LoadPkcs12(
                pfx, password, X509KeyStorageFlags.Exportable);

            var importDomains = CertificateEnrollmentPolicy.GetImportDomains(
                enrollment.Domain, enrollment.IsWildcard);
            var existing = await _apiManagement.GetCertificatesAsync();
            var newDomains = CertificateEnrollmentPolicy.FilterNewDomains(
                existing, importDomains, cert.Thumbprint);

            foreach (var domain in newDomains)
            {
                await _apiManagement.CreateCertificateAsync(new CertificateConfig
                {
                    Id = Guid.NewGuid().ToString(),
                    DomainName = domain,
                    CertificateType = "Pfx",
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = cert.NotAfter,
                    Subject = cert.Subject,
                    Issuer = cert.Issuer,
                    Thumbprint = cert.Thumbprint,
                    IsValid = CertificateValidity.IsWithinWindow(cert, DateTimeOffset.Now),
                    CertificateData = pfx,
                    Password = password,
                    EnrollmentId = enrollment.Id,
                });
            }

            enrollment.State = CertificateEnrollmentState.Synced;
            enrollment.LastThumbprint = cert.Thumbprint;
            enrollment.ExpiresAt = cert.NotAfter;
            enrollment.LastSyncAt = DateTimeOffset.UtcNow;
            enrollment.LastError = null;
            await _store.UpdateAsync(enrollment.Id, enrollment);

            _logger.LogInformation(
                "证书同步完成: Domain={Domain}, Thumbprint={Thumbprint}, NewRows={NewRows}",
                enrollment.Domain, cert.Thumbprint, newDomains.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "证书同步失败: Domain={Domain}", enrollment.Domain);
            enrollment.State = CertificateEnrollmentState.Failed;
            enrollment.LastError = ex.Message;
            await _store.UpdateAsync(enrollment.Id, enrollment);
            return false;
        }
    }

    /// <summary>Completed 事件：按域名匹配登记（未知域名忽略），回填外部 ID 后触发同步。</summary>
    public async Task HandleCompletedAsync(Guid certificateId, string domain, CancellationToken ct)
    {
        var normalized = CertificateEnrollmentPolicy.NormalizeDomain(domain);
        var enrollment = await _store.GetByDomainAsync(normalized);
        if (enrollment is null || !enrollment.Enabled)
        {
            return;
        }

        enrollment.ExternalCertificateId = certificateId;
        await _store.UpdateAsync(enrollment.Id, enrollment);
        await SyncAsync(enrollment.Id, ct);
    }

    /// <summary>Failed 事件：记录错误并把登记转 Failed。</summary>
    public async Task HandleFailedAsync(string domain, string errorMessage, CancellationToken ct)
    {
        var normalized = CertificateEnrollmentPolicy.NormalizeDomain(domain);
        var enrollment = await _store.GetByDomainAsync(normalized);
        if (enrollment is null)
        {
            return;
        }

        enrollment.State = CertificateEnrollmentState.Failed;
        enrollment.LastError = errorMessage;
        await _store.UpdateAsync(enrollment.Id, enrollment);
    }

    public Task<IReadOnlyList<CertificateEnrollmentDto>> ListAsync() => _store.GetAllAsync();

    /// <summary>移除登记：只解绑托管关系，不动已导入的证书。</summary>
    public async Task<bool> DeleteAsync(string id)
    {
        var enrollment = await _store.GetByIdAsync(id);
        if (enrollment is null)
        {
            return false;
        }

        await _store.DeleteAsync(id);
        return true;
    }

    private static string GeneratePfxPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(18);
        return Convert.ToBase64String(bytes);
    }
}

public class EnrollmentException : Exception
{
    public EnrollmentException(string message) : base(message)
    {
    }
}
