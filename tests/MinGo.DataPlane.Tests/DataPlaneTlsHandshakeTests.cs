using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Grpc;
using MinGo.DataPlane.Kestrel;

namespace MinGo.DataPlane.Tests;

/// <summary>
/// 真实 TLS 握手：验证数据面选择器接入 Kestrel 后的失败形态
/// （单元测试只覆盖"返回什么"，这里覆盖"客户端看到什么"）。
/// </summary>
public class DataPlaneTlsHandshakeTests : IAsyncLifetime
{
    private IHost _host = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        var provider = new DataPlaneConfigProvider(
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<DataPlaneConfigProvider>.Instance);
        provider.UpdateCertificates(new[] { CreateCertificateData("known.local") });

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseKestrel();
                web.UseUrls("https://127.0.0.1:0");
                web.ConfigureServices(services =>
                {
                    services.AddSingleton(provider);
                    services.AddCertificateServices();
                });
                web.Configure(app => app.Run(ctx => ctx.Response.WriteAsync("ok")));
            })
            .Build();

        _host.Services.GetRequiredService<DataPlaneCertificateSelector>().ReloadFromProvider();
        await _host.StartAsync();

        var addresses = _host.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses;
        _port = new Uri(addresses.First()).Port;
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private static CertificateData CreateCertificateData(string domain)
    {
        var notAfter = DateTimeOffset.UtcNow.AddDays(365);
        var notBefore = notAfter.AddDays(-365);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={domain}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);

        return new CertificateData
        {
            DomainName = domain,
            CertificateType = "Pfx",
            CertificateBytes = ByteString.CopyFrom(certificate.Export(X509ContentType.Pfx)),
            Password = "",
            Thumbprint = certificate.Thumbprint,
            ExpiresAtUnixMs = notAfter.ToUnixTimeMilliseconds()
        };
    }

    private async Task<SslStream> ConnectAsync(string targetHost)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _port);
        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        });
        return ssl;
    }

    [Fact]
    public async Task KnownSni_CompletesHandshake()
    {
        using var ssl = await ConnectAsync("known.local");

        Assert.True(ssl.IsAuthenticated);
        var cert = Assert.IsType<X509Certificate2>(ssl.RemoteCertificate);
        Assert.Equal("known.local", cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
    }

    [Fact]
    public async Task NoSni_CompletesHandshakeWithDefaultCertificate()
    {
        using var ssl = await ConnectAsync(targetHost: "");

        Assert.True(ssl.IsAuthenticated);
        var cert = Assert.IsType<X509Certificate2>(ssl.RemoteCertificate);
        Assert.Equal("known.local", cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
    }

    [Fact]
    public async Task UnknownSni_FailsHandshake()
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync("unknown.local"));

        Assert.True(
            exception is AuthenticationException or IOException,
            $"Unexpected exception type: {exception.GetType().Name}: {exception.Message}");
    }
}
