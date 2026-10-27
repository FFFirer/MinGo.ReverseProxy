using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Grpc;

namespace MinGo.DataPlane.Kestrel;

/// <summary>
/// 数据面证书扩展 - 从 DataPlaneConfigProvider 加载证书
/// 不直接访问数据库
/// </summary>
public class DataPlaneCertificateSelector
{
    private readonly DataPlaneConfigProvider _configProvider;
    private readonly ILogger<DataPlaneCertificateSelector> _logger;
    private readonly Dictionary<string, X509Certificate2> _certCache = new();
    private readonly object _lock = new();
    private X509Certificate2? _defaultCert;

    public DataPlaneCertificateSelector(DataPlaneConfigProvider configProvider, ILogger<DataPlaneCertificateSelector> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    /// <summary>
    /// 按 SNI 选择证书。
    /// <para>无 SNI（入参为空）→ 兜底证书；精确/通配命中 → 命中证书。</para>
    /// <para>有 SNI 但未命中 → 返回 null 让握手失败，不回退到名字不对的证书。</para>
    /// </summary>
    public X509Certificate2? SelectCertificate(string? domainName)
    {
        // 实测：无 SNI 的握手传入空字符串而非 null，两者必须都走兜底
        if (string.IsNullOrEmpty(domainName))
            return _defaultCert;

        if (TryFindInCache(domainName, out var cert))
            return cert;

        // Kestrel 只在 Debug 级别记录这次失败，这里补一条生产可见的告警
        _logger.LogWarning("No certificate configured for {Domain}; failing TLS handshake",
            NormalizeHost(domainName));
        return null;
    }

    private bool TryFindInCache(string host, out X509Certificate2? certificate)
    {
        var normalized = NormalizeHost(host);

        lock (_lock)
        {
            // 精确匹配
            if (_certCache.TryGetValue(normalized, out certificate))
                return true;

            // 通配符匹配
            foreach (var (key, cached) in _certCache)
            {
                if (key.StartsWith("*.") && normalized.EndsWith(key[1..], StringComparison.Ordinal))
                {
                    certificate = cached;
                    return true;
                }
            }
        }

        certificate = null;
        return false;
    }

    /// <summary>小写化并剥离端口；IPv6 字面量保留方括号形式。</summary>
    private static string NormalizeHost(string host)
    {
        var value = host.Trim().ToLowerInvariant();

        if (value.StartsWith('['))
        {
            var end = value.IndexOf(']');
            return end > 0 ? value[..(end + 1)] : value;
        }

        var colon = value.IndexOf(':');
        return colon > 0 ? value[..colon] : value;
    }

    public void ReloadFromProvider()
    {
        lock (_lock)
        {
            // 清理旧证书
            foreach (var cert in _certCache.Values)
                cert.Dispose();
            _certCache.Clear();
            _defaultCert = null;

            var certificates = _configProvider.CurrentCertificates;
            foreach (var certData in certificates)
            {
                try
                {
                    if (certData.CertificateBytes.IsEmpty) continue;

                    var isPfx = certData.CertificateType.Equals("Pfx", StringComparison.OrdinalIgnoreCase);
                    var certBytes = certData.CertificateBytes.ToArray();
                    var password = string.IsNullOrEmpty(certData.Password) ? null : certData.Password;

                    var cert = isPfx
                        ? X509CertificateLoader.LoadPkcs12(certBytes, password,
                            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable)
                        : X509CertificateLoader.LoadCertificate(certBytes);

                    var key = NormalizeHost(certData.DomainName);
                    _certCache[key] = cert;

                    WarnIfExpired(certData);

                    _logger.LogInformation("Loaded certificate for domain {Domain}, thumbprint: {Thumbprint}",
                        certData.DomainName, certData.Thumbprint);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load certificate for domain {Domain}", certData.DomainName);
                }
            }

            SelectDefaultCertificate();
        }
    }

    /// <summary>
    /// 兜底证书（无 SNI 时使用）的确定性规则：精确键 <c>localhost</c> 优先，
    /// 否则取缓存键字典序最小的一张（通配键同样参与排序）。
    /// </summary>
    private void SelectDefaultCertificate()
    {
        if (_certCache.Count == 0)
            return;

        var key = _certCache.ContainsKey("localhost")
            ? "localhost"
            : _certCache.Keys.OrderBy(k => k, StringComparer.Ordinal).First();

        _defaultCert = _certCache[key];
        _logger.LogInformation("Default certificate for SNI-less connections: {Domain}, thumbprint: {Thumbprint}",
            key, _defaultCert.Thumbprint);
    }

    /// <summary>
    /// 过期证书照常进缓存（客户端会拒绝，且拒绝信息更可读），只在日志里告警。
    /// </summary>
    private void WarnIfExpired(CertificateData certData)
    {
        if (certData.ExpiresAtUnixMs <= 0)
            return;

        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(certData.ExpiresAtUnixMs);
        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Certificate for {Domain} expired at {ExpiresAt:O}; serving it anyway",
                certData.DomainName, expiresAt);
        }
    }
}

public static class KestrelCertificateExtensions
{
    public static IServiceCollection AddCertificateServices(this IServiceCollection services)
    {
        services.AddSingleton<DataPlaneCertificateSelector>();

        // 注册 IConfigureOptions<KestrelServerOptions>，在 Kestrel 启动时将证书选择器挂载到 HTTPS 默认配置
        services.AddSingleton<IConfigureOptions<KestrelServerOptions>>(sp =>
        {
            var selector = sp.GetRequiredService<DataPlaneCertificateSelector>();
            return new ConfigureOptions<KestrelServerOptions>(options =>
            {
                options.ConfigureHttpsDefaults(httpsOpt =>
                {
                    httpsOpt.SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                          | System.Security.Authentication.SslProtocols.Tls13;

                    httpsOpt.ServerCertificateSelector = (context, domainName) =>
                        selector.SelectCertificate(domainName);
                });
            });
        });

        return services;
    }
}
