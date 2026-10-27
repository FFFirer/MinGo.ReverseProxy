using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Grpc;
using MinGo.DataPlane.Kestrel;

namespace MinGo.DataPlane.Tests;

internal static class TestCertificateFactory
{
    public static CertificateData Create(string domain, bool expired = false)
    {
        var notAfter = expired
            ? DateTimeOffset.UtcNow.AddDays(-30)
            : DateTimeOffset.UtcNow.AddDays(365);
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

    public static DataPlaneCertificateSelector CreateSelector(params CertificateData[] certificates)
    {
        var provider = new DataPlaneConfigProvider(
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<DataPlaneConfigProvider>.Instance);
        provider.UpdateCertificates(certificates);

        var selector = new DataPlaneCertificateSelector(
            provider, NullLogger<DataPlaneCertificateSelector>.Instance);
        selector.ReloadFromProvider();
        return selector;
    }
}
