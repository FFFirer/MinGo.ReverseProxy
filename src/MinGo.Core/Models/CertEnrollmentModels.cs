namespace MinGo.Core.Models;

/// <summary>
/// 证书接入登记状态机：Unrequested（登记未申请/未同步）→ Pending（已申请，等签发）
/// → Synced（指纹同步成功）；失败 → Failed（可手动重试）；Disabled（停用）。
/// </summary>
public enum CertificateEnrollmentState
{
    Unrequested,
    Pending,
    Synced,
    Failed,
    Disabled,
}

public class CertificateEnrollmentDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Domain { get; set; } = string.Empty;
    public bool IsWildcard { get; set; }
    public bool UseStaging { get; set; }
    public Guid? ExternalCertificateId { get; set; }
    public CertificateEnrollmentState State { get; set; } = CertificateEnrollmentState.Unrequested;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastThumbprint { get; set; }
    public string? LastError { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class CertManagerSettingsDto
{
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>读取时返回掩码值（如 abcd***）；掩码或空白在保存时视为不修改。</summary>
    public string ApiKey { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>是否已配置（BaseUrl 与 ApiKey 均非空）。</summary>
    public bool Configured { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>登记请求体（POST /api/cert-enrollments）。</summary>
public class RegisterCertificateEnrollmentRequest
{
    public string Domain { get; set; } = string.Empty;
    public bool IsWildcard { get; set; }
    public bool UseStaging { get; set; }
}
