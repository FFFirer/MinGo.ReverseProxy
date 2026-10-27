using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MinGo.Core.Models;

namespace MinGo.Core.Tests.Certificates;

public class CertificateValidityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static X509Certificate2 CreateCertificate(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=test.local", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    [Fact]
    public void IsWithinWindow_CurrentCertificate_ReturnsTrue()
    {
        using var cert = CreateCertificate(Now.AddDays(-1), Now.AddDays(1));

        Assert.True(CertificateValidity.IsWithinWindow(cert, Now));
    }

    [Fact]
    public void IsWithinWindow_ExpiredCertificate_ReturnsFalse()
    {
        using var cert = CreateCertificate(Now.AddDays(-30), Now.AddDays(-1));

        Assert.False(CertificateValidity.IsWithinWindow(cert, Now));
    }

    [Fact]
    public void IsWithinWindow_NotYetValidCertificate_ReturnsFalse()
    {
        using var cert = CreateCertificate(Now.AddDays(1), Now.AddDays(30));

        Assert.False(CertificateValidity.IsWithinWindow(cert, Now));
    }

    [Fact]
    public void HasNotExpired_NullExpiry_IsLongLived()
    {
        Assert.True(CertificateValidity.HasNotExpired(null, Now));
    }

    [Fact]
    public void HasNotExpired_PastExpiry_ReturnsFalse()
    {
        Assert.False(CertificateValidity.HasNotExpired(Now.AddSeconds(-1), Now));
    }

    [Fact]
    public void HasNotExpired_FutureExpiry_ReturnsTrue()
    {
        Assert.True(CertificateValidity.HasNotExpired(Now.AddSeconds(1), Now));
    }
}
