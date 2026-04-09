using System.Security.Cryptography.X509Certificates;
using MinGo.Core.Models;

namespace MinGo.Core.Interfaces;

/// <summary>
/// 证书管理器接口
/// 根据请求域名选择合适的证书
/// </summary>
public interface ICertificateManager
{
    /// <summary>
    /// 根据域名获取匹配的证书（异步）
    /// </summary>
    /// <param name="domainName">请求的域名</param>
    /// <returns>匹配的证书，如果找不到则返回开发证书（开发环境）或 null</returns>
    Task<X509Certificate2?> GetCertificateAsync(string domainName);

    /// <summary>
    /// 根据域名获取匹配的证书（同步，用于 TLS 握手回调）
    /// </summary>
    /// <param name="domainName">请求的域名（SNI）</param>
    /// <returns>匹配的证书，如果找不到则返回兜底证书，永不返回 null</returns>
    X509Certificate2? GetCertificate(string domainName);

    /// <summary>
    /// 获取默认/兜底证书
    /// </summary>
    /// <returns>兜底证书，永不返回 null</returns>
    X509Certificate2? GetDefaultCertificate();

    /// <summary>
    /// 重新加载证书缓存
    /// </summary>
    Task ReloadCertificatesAsync();

    /// <summary>
    /// 获取所有已加载的证书信息
    /// </summary>
    /// <returns>证书信息列表</returns>
    Task<IEnumerable<CertificateInfo>> GetCertificateInfosAsync();
}

/// <summary>
/// 证书信息摘要
/// </summary>
public class CertificateInfo
{
    public string DomainName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsValid { get; set; }
}
