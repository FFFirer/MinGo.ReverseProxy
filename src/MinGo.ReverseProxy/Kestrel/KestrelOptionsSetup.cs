using System;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Server.Kestrel.Core;


namespace MinGo.ReverseProxy.Kestrel;

public class KestrelOptionsSetup : IConfigureOptions<KestrelServerOptions>
{
    private readonly IServerCertificateSelector _selector;

    public KestrelOptionsSetup(IServerCertificateSelector selector)
    {
        _selector = selector;
    }

    public void Configure(KestrelServerOptions options)
    {
        options.ConfigureHttpsDefaults(options => options.UseCerts(_selector));
    }
}
