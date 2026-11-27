namespace MinGo.Core.Interfaces;

/// <summary>
/// CertManager /api/external 客户端（实现在 Infrastructure，凭据来自落库设置）。
/// </summary>
public interface ICertManagerClient
{
    /// <summary>发起申请，返回 CertManager 侧证书记录 ID。</summary>
    Task<Guid> RequestCertificateAsync(string domain, bool isWildcard, bool useStaging, CancellationToken ct);

    /// <summary>查询申请状态；证书不存在返回 null。</summary>
    Task<ExternalCertificateStatus?> GetStatusAsync(Guid certificateId, CancellationToken ct);

    /// <summary>按域名下载最新有效证书（PFX）；域名无证书返回 null。</summary>
    Task<byte[]?> DownloadPfxAsync(string domain, string password, CancellationToken ct);

    /// <summary>探测域名是否已有可下载证书（true=有，false=404 无证书）。</summary>
    Task<bool> ProbeHasCertificateAsync(string domain, CancellationToken ct);

    /// <summary>连通性测试（404=正常，401=ApiKey 错）。</summary>
    Task<CertManagerConnectionResult> TestConnectionAsync(CancellationToken ct);
}

public enum CertManagerError
{
    NotConfigured,
    Unauthorized,
    RequestFailed,
}

public class CertManagerException : Exception
{
    public CertManagerError Kind { get; }

    public CertManagerException(string message, CertManagerError kind) : base(message)
    {
        Kind = kind;
    }
}

/// <summary>CertManager 侧申请状态。</summary>
public sealed record ExternalCertificateStatus(
    Guid CertificateId,
    string Domain,
    string Status,
    string AcmeStatus,
    string? AcmeStatusMessage,
    DateTime? ExpiresAt);

public sealed record CertManagerConnectionResult(bool Ok, string Message);
