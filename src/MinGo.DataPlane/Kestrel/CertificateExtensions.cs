using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;
using MinGo.DataPlane.ConfigSync;

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

    public X509Certificate2? SelectCertificate(string? domainName)
    {
        if (string.IsNullOrEmpty(domainName))
            return _defaultCert;

        var normalized = domainName.ToLowerInvariant();

        lock (_lock)
        {
            // 精确匹配
            if (_certCache.TryGetValue(normalized, out var cert))
                return cert;

            // 通配符匹配
            foreach (var (key, cached) in _certCache)
            {
                if (key.StartsWith("*.") && normalized.EndsWith(key[1..]))
                    return cached;
            }
        }

        return _defaultCert;
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

                    var cert = string.IsNullOrEmpty(certData.Password)
                        ? X509CertificateLoader.LoadCertificate(certData.CertificateBytes.ToArray())
                        : X509CertificateLoader.LoadPkcs12(certData.CertificateBytes.ToArray(), certData.Password,
                            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

                    var key = certData.DomainName.ToLowerInvariant();
                    _certCache[key] = cert;

                    _defaultCert ??= cert;

                    _logger.LogInformation("Loaded certificate for domain {Domain}, thumbprint: {Thumbprint}",
                        certData.DomainName, certData.Thumbprint);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load certificate for domain {Domain}", certData.DomainName);
                }
            }
        }
    }
}

public static class KestrelCertificateExtensions
{
    public static IServiceCollection AddCertificateServices(this IServiceCollection services)
    {
        services.AddSingleton<DataPlaneCertificateSelector>();
        return services;
    }

    public static void ConfigureKestrelHttps(this KestrelServerOptions options, bool isDev)
    {
        options.ConfigureHttpsDefaults(httpsOpt =>
        {
            httpsOpt.SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                  | System.Security.Authentication.SslProtocols.Tls13;

            httpsOpt.ServerCertificateSelector = (context, domainName) =>
            {
                var selector = context?.Features.Get<DataPlaneCertificateSelector>();
                return selector?.SelectCertificate(domainName);
            };
        });
    }
}
