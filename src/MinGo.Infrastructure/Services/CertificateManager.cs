using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using MinGo.Infrastructure.Data;
using MinGo.ReverseProxy.Kestrel;

namespace MinGo.Infrastructure.Services;

/// <summary>
/// 证书管理器实现
/// 根据请求域名从数据库中选择匹配的证书
/// </summary>
public class CertificateManager : ICertificateManager, IServerCertificateSelector
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<CertificateManager> _logger;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ConcurrentDictionary<string, X509Certificate2> _certificateCache = new();
    private readonly ConcurrentDictionary<string, CertificateInfo> _certificateInfoCache = new();
    private X509Certificate2? _devCertificate;
    private X509Certificate2? _fallbackCertificate;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private const string DevCertificateDomain = "localhost";
    private const string WildcardPrefix = "*.";

    public CertificateManager(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<CertificateManager> logger,
        IHostEnvironment hostEnvironment)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
    }

    /// <summary>
    /// 根据域名获取匹配的证书（同步版本，用于 TLS 握手回调）
    /// </summary>
    public X509Certificate2? GetCertificate(string domainName)
    {
        if (string.IsNullOrEmpty(domainName))
        {
            return GetDefaultCertificate();
        }

        var normalizedDomain = NormalizeDomain(domainName);

        // 1. 精确匹配
        if (_certificateCache.TryGetValue(normalizedDomain, out var exactMatch))
        {
            _logger.LogDebug("Certificate found for domain {Domain} (exact match)", normalizedDomain);
            return exactMatch;
        }

        // 2. 通配符匹配
        var wildcardCert = FindWildcardMatch(normalizedDomain);
        if (wildcardCert != null)
        {
            _logger.LogDebug("Certificate found for domain {Domain} (wildcard match)", normalizedDomain);
            return wildcardCert;
        }

        // 3. 开发环境使用开发证书兜底
        if (_hostEnvironment.IsDevelopment())
        {
            var devCert = GetDevCertificate();
            if (devCert != null)
            {
                _logger.LogInformation("Using development certificate for domain {Domain}", normalizedDomain);
                return devCert;
            }
        }

        // 4. 静默降级到默认证书
        _logger.LogDebug("No certificate found for domain {Domain}, using fallback certificate", normalizedDomain);
        return GetDefaultCertificate();
    }

    /// <summary>
    /// 根据域名获取匹配的证书（异步）
    /// 匹配顺序：
    /// 1. 精确匹配域名
    /// 2. 通配符匹配（如 *.example.com 匹配 sub.example.com）
    /// 3. 开发环境使用开发证书兜底
    /// 4. 返回默认证书
    /// </summary>
    public Task<X509Certificate2?> GetCertificateAsync(string domainName)
    {
        return Task.FromResult(GetCertificate(domainName));
    }

    /// <summary>
    /// 重新加载证书缓存
    /// </summary>
    public async Task ReloadCertificatesAsync()
    {
        await _reloadLock.WaitAsync();
        try
        {
            _logger.LogInformation("Reloading certificate cache...");
            
            // 清理旧证书
            foreach (var cert in _certificateCache.Values)
            {
                cert.Dispose();
            }
            _certificateCache.Clear();
            _certificateInfoCache.Clear();

            // 重新加载证书
            await LoadCertificatesFromDatabaseAsync();

            // 加载开发证书
            LoadDevCertificate();

            _logger.LogInformation("Certificate cache reloaded. Loaded {Count} certificates", _certificateCache.Count);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>
    /// 获取所有已加载的证书信息
    /// </summary>
    public Task<IEnumerable<CertificateInfo>> GetCertificateInfosAsync()
    {
        return Task.FromResult<IEnumerable<CertificateInfo>>(_certificateInfoCache.Values.ToList());
    }

    /// <summary>
    /// 从数据库加载证书
    /// </summary>
    private async Task LoadCertificatesFromDatabaseAsync()
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

            var certificates = await dbContext.Certificates
                .Where(c => c.IsValid && c.CertificateData != null)
                .ToListAsync();

            foreach (var certEntity in certificates)
            {
                try
                {
                    var certData = certEntity.CertificateData;
                    if (certData == null || certData.Length == 0)
                    {
                        _logger.LogWarning("Certificate {Id} has no certificate data", certEntity.Id);
                        continue;
                    }

                    X509Certificate2 cert;
                    if (!string.IsNullOrEmpty(certEntity.Password))
                    {
                        cert = new X509Certificate2(certData, certEntity.Password, 
                            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
                    }
                    else
                    {
                        cert = new X509Certificate2(certData);
                    }

                    // 使用域名作为缓存键
                    var cacheKey = certEntity.DomainName.ToLowerInvariant();
                    _certificateCache.TryAdd(cacheKey, cert);

                    // 缓存证书信息
                    var info = new CertificateInfo
                    {
                        DomainName = certEntity.DomainName,
                        Subject = certEntity.Subject ?? cert.Subject,
                        Thumbprint = certEntity.Thumbprint ?? cert.Thumbprint,
                        ExpiresAt = certEntity.ExpiresAt ?? cert.NotAfter,
                        IsValid = certEntity.IsValid
                    };
                    _certificateInfoCache.TryAdd(cacheKey, info);

                    _logger.LogDebug("Loaded certificate for domain {Domain}, thumbprint: {Thumbprint}", 
                        certEntity.DomainName, cert.Thumbprint);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load certificate {Id} for domain {Domain}", 
                        certEntity.Id, certEntity.DomainName);
                }
            }

            // 设置兜底证书（如果有多个证书，选择第一个）
            SetFallbackCertificate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load certificates from database");
        }
    }

    /// <summary>
    /// 加载开发证书
    /// </summary>
    private void LoadDevCertificate()
    {
        // 尝试从配置或文件加载开发证书
        // 常见开发证书路径
        var devCertPaths = new[]
        {
            Path.Combine(_hostEnvironment.ContentRootPath, "certs", "dev.pfx"),
            Path.Combine(_hostEnvironment.ContentRootPath, "certs", "localhost.pfx"),
            Path.Combine(AppContext.BaseDirectory, "certs", "dev.pfx"),
            "dev.pfx",
            "localhost.pfx"
        };

        foreach (var certPath in devCertPaths)
        {
            if (File.Exists(certPath))
            {
                try
                {
                    // 开发证书通常没有密码或使用 "development" 密码
                    var certBytes = File.ReadAllBytes(certPath);
                    _devCertificate = new X509Certificate2(certBytes, "", 
                        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
                    _logger.LogInformation("Development certificate loaded from {Path}, thumbprint: {Thumbprint}", 
                        certPath, _devCertificate.Thumbprint);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to load dev certificate from {Path}", certPath);
                }
            }
        }

        // 如果没有找到文件，尝试生成自签名证书（仅开发环境）
        if (_hostEnvironment.IsDevelopment())
        {
            _devCertificate = CreateSelfSignedCertificate(DevCertificateDomain);
            if (_devCertificate != null)
            {
                _logger.LogInformation("Created self-signed development certificate, thumbprint: {Thumbprint}", 
                    _devCertificate.Thumbprint);
            }
        }
    }

    /// <summary>
    /// 创建自签名证书（仅用于开发环境）
    /// </summary>
    private X509Certificate2? CreateSelfSignedCertificate(string domainName)
    {
        try
        {
            using var rsa = RSA.Create(2048);
            var distinguishedName = new X500DistinguishedName($"CN={domainName}");
            var request = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            // 添加 Subject Alternative Names
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(domainName);
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
            request.CertificateExtensions.Add(sanBuilder.Build());

            // 自签名，有效期 1 年
            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddYears(1));

            // 导出并重新导入以使其可导出
            var pfxBytes = cert.Export(X509ContentType.Pfx, "development");
            return X509CertificateLoader.LoadPkcs12(pfxBytes, "development", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            // return new X509Certificate2(pfxBytes, "development",
            //     X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create self-signed certificate for {Domain}", domainName);
            return null;
        }
    }

    /// <summary>
    /// 查找通配符匹配的证书
    /// </summary>
    private X509Certificate2? FindWildcardMatch(string domainName)
    {
        foreach (var (cachedDomain, cert) in _certificateCache)
        {
            if (cachedDomain.StartsWith(WildcardPrefix))
            {
                var wildcardPattern = cachedDomain.Substring(WildcardPrefix.Length);
                if (MatchWildcard(domainName, wildcardPattern))
                {
                    return cert;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 通配符匹配
    /// </summary>
    private static bool MatchWildcard(string domain, string pattern)
    {
        // *.example.com 匹配 sub.example.com 但不匹配 example.com 本身
        if (domain.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
        {
            // 确保前面有子域名（对于 *.example.com，example.com 不匹配）
            var remainder = domain.Substring(0, domain.Length - pattern.Length);
            return remainder.Length > 0 && remainder.EndsWith(".", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// 规范化域名（移除端口号）
    /// </summary>
    private static string NormalizeDomain(string domain)
    {
        var normalized = domain.ToLowerInvariant();
        
        // 移除端口号
        var portIndex = normalized.LastIndexOf(':');
        if (portIndex > 0)
        {
            normalized = normalized.Substring(0, portIndex);
        }

        return normalized;
    }

    /// <summary>
    /// 设置兜底证书
    /// </summary>
    private void SetFallbackCertificate()
    {
        if (_certificateCache.Count > 0)
        {
            _fallbackCertificate = _certificateCache.Values.FirstOrDefault();
        }
    }

    /// <summary>
    /// 获取开发证书
    /// </summary>
    private X509Certificate2? GetDevCertificate()
    {
        if (_devCertificate == null)
        {
            LoadDevCertificate();
        }
        return _devCertificate;
    }

    /// <summary>
    /// 获取默认/兜底证书
    /// </summary>
    public X509Certificate2? GetDefaultCertificate()
    {
        return _fallbackCertificate ?? GetDevCertificate();
    }

    public X509Certificate2? Select(ConnectionContext? context, string? domainName)
    {
        if(domainName is null) return null;

        return GetCertificate(domainName);
    }
}
