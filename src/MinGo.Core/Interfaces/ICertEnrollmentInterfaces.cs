using MinGo.Core.Models;

namespace MinGo.Core.Interfaces;

/// <summary>
/// CertManager 远程接入配置存储（单行，Id = default）。
/// </summary>
public interface ICertManagerSettingsService
{
    /// <summary>读取配置（ApiKey 脱敏）。</summary>
    Task<CertManagerSettingsDto> GetAsync();

    /// <summary>读取配置（ApiKey 明文，仅供出站调用方使用）。</summary>
    Task<CertManagerSettingsDto> GetWithApiKeyAsync();

    /// <summary>保存配置；ApiKey 为空白或掩码时保持不变。</summary>
    Task<CertManagerSettingsDto> SaveAsync(CertManagerSettingsDto dto);
}

/// <summary>
/// 证书自动接入登记存储。
/// </summary>
public interface ICertificateEnrollmentStore
{
    Task<IReadOnlyList<CertificateEnrollmentDto>> GetAllAsync();

    Task<CertificateEnrollmentDto?> GetByIdAsync(string id);

    Task<CertificateEnrollmentDto?> GetByDomainAsync(string domain);

    Task<CertificateEnrollmentDto> CreateAsync(CertificateEnrollmentDto dto);

    Task<CertificateEnrollmentDto?> UpdateAsync(string id, CertificateEnrollmentDto dto);

    Task DeleteAsync(string id);
}
