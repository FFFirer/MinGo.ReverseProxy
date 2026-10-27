using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Grpc;
using MinGo.DataPlane.Kestrel;
using Moq;

namespace MinGo.DataPlane.Tests;

public class DataPlaneCertificateSelectorTests
{
    private static CertificateData CreateCertificate(string domain, bool expired = false)
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

    private static DataPlaneCertificateSelector CreateSelector(
        out Mock<ILogger<DataPlaneCertificateSelector>> logger,
        params CertificateData[] certificates)
    {
        var provider = new DataPlaneConfigProvider(
            new ServiceCollection().BuildServiceProvider(),
            new Mock<ILogger<DataPlaneConfigProvider>>().Object);
        provider.UpdateCertificates(certificates);

        logger = new Mock<ILogger<DataPlaneCertificateSelector>>();
        var selector = new DataPlaneCertificateSelector(provider, logger.Object);
        selector.ReloadFromProvider();
        return selector;
    }

    private static void VerifyLog<T>(
        Mock<ILogger<T>> logger, LogLevel level, string contains, Times times)
    {
        logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(contains)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    [Fact]
    public void UnknownSni_ReturnsNull_AndLogsWarning()
    {
        var selector = CreateSelector(out var logger, CreateCertificate("known.local"));

        Assert.Null(selector.SelectCertificate("unknown.local"));
        VerifyLog(logger, LogLevel.Warning, "No certificate configured", Times.Once());
    }

    [Fact]
    public void NoSni_ReturnsDefaultCertificate()
    {
        var selector = CreateSelector(out _, CreateCertificate("known.local"));

        var viaNull = selector.SelectCertificate(null);
        var viaEmpty = selector.SelectCertificate("");

        Assert.NotNull(viaNull);
        Assert.Same(viaNull, viaEmpty);
        Assert.Contains("known.local", viaNull!.Subject);
    }

    [Fact]
    public void DefaultCertificate_PrefersLocalhost()
    {
        var selector = CreateSelector(
            out _,
            CreateCertificate("aaa.local"),
            CreateCertificate("localhost"),
            CreateCertificate("zzz.local"));

        var defaultCert = selector.SelectCertificate("");

        Assert.NotNull(defaultCert);
        Assert.Contains("localhost", defaultCert!.Subject);
    }

    [Fact]
    public void DefaultCertificate_WithoutLocalhost_IsLexicographicallySmallest()
    {
        var selector = CreateSelector(
            out _,
            CreateCertificate("zeta.local"),
            CreateCertificate("alpha.local"));

        var defaultCert = selector.SelectCertificate(null);

        Assert.NotNull(defaultCert);
        Assert.Contains("alpha.local", defaultCert!.Subject);
    }

    [Fact]
    public void Wildcard_MatchesSubdomain()
    {
        var selector = CreateSelector(
            out _,
            CreateCertificate("*.example.com"),
            CreateCertificate("fallback.local"));

        var matched = selector.SelectCertificate("app.example.com");

        Assert.NotNull(matched);
        Assert.Contains("*.example.com", matched!.Subject);
    }

    [Fact]
    public void Wildcard_DoesNotMatchApex_AndNoFallback()
    {
        var selector = CreateSelector(
            out _,
            CreateCertificate("*.example.com"),
            CreateCertificate("fallback.local"));

        Assert.Null(selector.SelectCertificate("example.com"));
    }

    [Fact]
    public void ExpiredCertificate_IsCachedAndWarns()
    {
        var selector = CreateSelector(out var logger, CreateCertificate("old.local", expired: true));

        var selected = selector.SelectCertificate("old.local");

        Assert.NotNull(selected);
        Assert.Contains("old.local", selected!.Subject);
        VerifyLog(logger, LogLevel.Warning, "expired", Times.Once());
    }

    [Fact]
    public void EmptyCertificateBytes_AreSkipped()
    {
        var empty = new CertificateData
        {
            DomainName = "empty.local",
            CertificateType = "Pfx",
            CertificateBytes = ByteString.Empty,
            ExpiresAtUnixMs = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds()
        };
        var selector = CreateSelector(out _, empty);

        Assert.Null(selector.SelectCertificate("empty.local"));
        Assert.Null(selector.SelectCertificate(""));
    }
}
