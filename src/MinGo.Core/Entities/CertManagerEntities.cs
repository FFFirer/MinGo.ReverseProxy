using MinGo.Core.Models;

namespace MinGo.Core.Entities;

/// <summary>
/// CertManager 远程接入配置（单行，Id = default）。ApiKey 由设置页写入，存储时加密、读取时脱敏。
/// </summary>
public class CertManagerSettingsEntity
{
    public const string DefaultId = "default";

    public string Id { get; set; } = DefaultId;

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>CertManager /api/external 的 API Key（DataProtection 加密存储）。</summary>
    public string ApiKey { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 证书自动接入登记：域名 → CertManager 全托管申请/续期 → 事件同步 → 导入 ApiCertificates。
/// 域名唯一（不区分大小写）；仅移除登记不会动已导入证书。
/// </summary>
public class CertificateEnrollmentEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Domain { get; set; } = string.Empty;

    public bool IsWildcard { get; set; }

    public bool UseStaging { get; set; }

    /// <summary>CertManager 侧证书记录 ID（申请成功后回填）。</summary>
    public Guid? ExternalCertificateId { get; set; }

    public CertificateEnrollmentState State { get; set; } = CertificateEnrollmentState.Unrequested;

    /// <summary>最近一次成功导入的证书到期时间（展示用，实际判定以 ApiCertificates 为准）。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? LastSyncAt { get; set; }

    /// <summary>最近导入证书指纹，与 DomainName 共同构成幂等键。</summary>
    public string? LastThumbprint { get; set; }

    public string? LastError { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
