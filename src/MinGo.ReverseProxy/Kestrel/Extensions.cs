using System;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Options;
using MinGo.Core.Interfaces;
using MinGo.Infrastructure.Services;

namespace MinGo.ReverseProxy.Kestrel;

public static class KestrelExtensions
{
    public static IServiceCollection AddCertificateServices(this IServiceCollection services)
    {
        // 注册证书管理器
        services.AddTransient<IConfigureOptions<KestrelServerOptions>, KestrelOptionsSetup>();
        services.AddSingleton<CertificateManager>();
        services.AddSingleton<ICertificateManager>(x => x.GetRequiredService<CertificateManager>());
        services.AddSingleton<IServerCertificateSelector>(x => x.GetRequiredService<CertificateManager>());

        return services;
    }

    public static HttpsConnectionAdapterOptions UseCerts(
        this HttpsConnectionAdapterOptions httpsOptions,
        IServiceProvider applicationServices)
    {
        var selector = applicationServices.GetService<IServerCertificateSelector>();

        if (selector is null)
        {
            throw new InvalidOperationException("没有注入IServerCertificateSelector实现");
        }

        return httpsOptions.UseCerts(selector);
    }

    public static HttpsConnectionAdapterOptions UseCerts(this HttpsConnectionAdapterOptions httpsOptions, IServerCertificateSelector certificateSelector)
    {
        // TLS-alpn

        httpsOptions.UseServerCertificateSelector(certificateSelector);
        return httpsOptions;
    }

    /// <summary>
    /// Configure HTTPS certificates dynamically with an implementation of <see cref="IServerCertificateSelector"/>.
    /// </summary>
    /// <param name="httpsOptions">The HTTPS configuration</param>
    /// <param name="certificateSelector">The server certificate selector.</param>
    /// <returns>The HTTPS configuration</returns>
    public static HttpsConnectionAdapterOptions UseServerCertificateSelector(
        this HttpsConnectionAdapterOptions httpsOptions,
        IServerCertificateSelector certificateSelector)
    {
        var fallbackSelector = httpsOptions.ServerCertificateSelector;
        httpsOptions.ServerCertificateSelector = (connectionContext, domainName) =>
        {
            var primaryCert = certificateSelector.Select(connectionContext!, domainName);

            if (primaryCert is null)
            {
                return fallbackSelector?.Invoke(connectionContext, domainName);
            }
            // fallback to the original selector if the injected selector fails to find a certificate.
            return primaryCert;
        };

        return httpsOptions;
    }
}
