using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using MinGo.Application.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using Moq;

namespace MinGo.ControlPlane.Api.Tests;

public class CertificateEnrollmentServiceTests
{
    private static readonly CertificateEnrollmentDto Enrollment = new()
    {
        Id = "enroll-1",
        Domain = "example.com",
        IsWildcard = false,
        State = CertificateEnrollmentState.Unrequested,
    };

    private readonly Mock<ICertificateEnrollmentStore> _store = new();
    private readonly Mock<ICertManagerClient> _client = new();
    private readonly Mock<IApiManagementService> _apiManagement = new();

    private CertificateEnrollmentService CreateService() =>
        new(_store.Object, _client.Object, _apiManagement.Object,
            NullLogger<CertificateEnrollmentService>.Instance);

    /// <summary>自签测试证书；Export(password) 用服务生成的密码重导出 PFX。</summary>
    private sealed class TestPfx : IDisposable
    {
        public X509Certificate2 Cert { get; }

        public string Thumbprint => Cert.Thumbprint;

        public TestPfx()
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=example.com", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("example.com");
            san.AddDnsName("*.example.com");
            req.CertificateExtensions.Add(san.Build());
            Cert = req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(89));
        }

        public byte[] Export(string password) => Cert.Export(X509ContentType.Pfx, password);

        public void Dispose() => Cert.Dispose();
    }

    /// <summary>DownloadPfxAsync 用服务传入的密码重导出同一证书，返回后即可用 Thumbprint 预置存量证书。</summary>
    private TestPfx ArrangePfxDownload()
    {
        var pfx = new TestPfx();
        _client.Setup(c => c.DownloadPfxAsync("example.com", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string password, CancellationToken _) => pfx.Export(password));
        return pfx;
    }

    // ── RegisterAsync ──

    [Fact]
    public async Task Register_ProbeHasCertificate_TriggersSyncWithoutRequest()
    {
        var created = new CertificateEnrollmentDto
        {
            Id = "enroll-1",
            Domain = "example.com",
            State = CertificateEnrollmentState.Unrequested,
        };
        _store.Setup(s => s.GetByDomainAsync("example.com")).ReturnsAsync((CertificateEnrollmentDto?)null);
        _store.Setup(s => s.CreateAsync(It.IsAny<CertificateEnrollmentDto>())).ReturnsAsync(created);
        _store.Setup(s => s.GetByIdAsync("enroll-1")).ReturnsAsync(created);
        _client.Setup(c => c.ProbeHasCertificateAsync("example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // 探测命中 → SyncAsync：无 PFX 可下载，登记保持原状态但不申请
        _client.Setup(c => c.DownloadPfxAsync("example.com", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await CreateService().RegisterAsync(new RegisterCertificateEnrollmentRequest
        {
            Domain = "Example.COM",
        }, CancellationToken.None);

        _client.Verify(c => c.RequestCertificateAsync(
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal("example.com", result.Domain);
    }

    [Fact]
    public async Task Register_ProbeMiss_RequestsCertificateAndGoesPending()
    {
        var created = new CertificateEnrollmentDto
        {
            Id = "enroll-1",
            Domain = "example.com",
            State = CertificateEnrollmentState.Unrequested,
        };
        _store.Setup(s => s.GetByDomainAsync("example.com")).ReturnsAsync((CertificateEnrollmentDto?)null);
        _store.Setup(s => s.CreateAsync(It.IsAny<CertificateEnrollmentDto>())).ReturnsAsync(created);
        _store.Setup(s => s.GetByIdAsync("enroll-1")).ReturnsAsync(created);
        _client.Setup(c => c.ProbeHasCertificateAsync("example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _client.Setup(c => c.RequestCertificateAsync("example.com", true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var result = await CreateService().RegisterAsync(new RegisterCertificateEnrollmentRequest
        {
            Domain = "example.com",
            IsWildcard = true,
        }, CancellationToken.None);

        Assert.Equal(CertificateEnrollmentState.Pending, result.State);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.ExternalCertificateId);
        _store.Verify(s => s.UpdateAsync("enroll-1", It.IsAny<CertificateEnrollmentDto>()), Times.Once);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("*.example.com")]
    [InlineData("")]
    public async Task Register_InvalidDomain_Throws(string domain)
    {
        await Assert.ThrowsAsync<EnrollmentException>(() =>
            CreateService().RegisterAsync(new RegisterCertificateEnrollmentRequest { Domain = domain },
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_DuplicateDomain_Throws()
    {
        _store.Setup(s => s.GetByDomainAsync("example.com")).ReturnsAsync(Enrollment);

        await Assert.ThrowsAsync<EnrollmentException>(() =>
            CreateService().RegisterAsync(new RegisterCertificateEnrollmentRequest { Domain = "example.com" },
                CancellationToken.None));
    }

    [Fact]
    public async Task Register_RequestFailure_MarksFailedWithError()
    {
        var created = new CertificateEnrollmentDto
        {
            Id = "enroll-1",
            Domain = "example.com",
            State = CertificateEnrollmentState.Unrequested,
        };
        _store.Setup(s => s.GetByDomainAsync("example.com")).ReturnsAsync((CertificateEnrollmentDto?)null);
        _store.Setup(s => s.CreateAsync(It.IsAny<CertificateEnrollmentDto>())).ReturnsAsync(created);
        _store.Setup(s => s.GetByIdAsync("enroll-1")).ReturnsAsync(created);
        _client.Setup(c => c.ProbeHasCertificateAsync("example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _client.Setup(c => c.RequestCertificateAsync(
                "example.com", It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DNS 验证超时"));

        var result = await CreateService().RegisterAsync(new RegisterCertificateEnrollmentRequest
        {
            Domain = "example.com",
        }, CancellationToken.None);

        Assert.Equal(CertificateEnrollmentState.Failed, result.State);
        Assert.Contains("DNS 验证超时", result.LastError);
    }

    // ── SyncAsync ──

    [Fact]
    public async Task Sync_MissingEnrollment_ReturnsFalse()
    {
        _store.Setup(s => s.GetByIdAsync("nope")).ReturnsAsync((CertificateEnrollmentDto?)null);

        Assert.False(await CreateService().SyncAsync("nope", CancellationToken.None));
    }

    [Fact]
    public async Task Sync_DisabledEnrollment_ReturnsFalseWithoutDownload()
    {
        var disabled = new CertificateEnrollmentDto { Id = "e1", Domain = "example.com", Enabled = false };
        _store.Setup(s => s.GetByIdAsync("e1")).ReturnsAsync(disabled);

        Assert.False(await CreateService().SyncAsync("e1", CancellationToken.None));
        _client.Verify(c => c.DownloadPfxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Sync_NoCertificateAvailable_ReturnsFalse()
    {
        _store.Setup(s => s.GetByIdAsync("enroll-1")).ReturnsAsync(Enrollment);
        _client.Setup(c => c.DownloadPfxAsync("example.com", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        Assert.False(await CreateService().SyncAsync("enroll-1", CancellationToken.None));
        _store.Verify(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<CertificateEnrollmentDto>()), Times.Never);
    }

    [Fact]
    public async Task Sync_DownloadsAndImportsWildcardAsTwoRows()
    {
        var wildcard = new CertificateEnrollmentDto
        {
            Id = "enroll-1",
            Domain = "example.com",
            IsWildcard = true,
            State = CertificateEnrollmentState.Pending,
        };
        using var pfx = ArrangePfxDownload();
        _store.Setup(s => s.GetByIdAsync("enroll-1")).ReturnsAsync(wildcard);
        _apiManagement.Setup(a => a.GetCertificatesAsync()).ReturnsAsync(Array.Empty<CertificateConfig>());

        var created = new List<CertificateConfig>();
        _apiManagement.Setup(a => a.CreateCertificateAsync(It.IsAny<CertificateConfig>()))
            .Callback((CertificateConfig c) => created.Add(c))
            .ReturnsAsync((CertificateConfig c) => c);

        var ok = await CreateService().SyncAsync("enroll-1", CancellationToken.None);

        Assert.True(ok, wildcard.LastError);
        Assert.Equal(2, created.Count);
        Assert.Contains(created, c => c.DomainName == "example.com");
        Assert.Contains(created, c => c.DomainName == "*.example.com");
        Assert.All(created, c => Assert.Equal("Pfx", c.CertificateType));
        Assert.All(created, c => Assert.NotNull(c.Password));
        Assert.All(created, c => Assert.NotNull(c.CertificateData));
        Assert.Equal(pfx.Thumbprint, wildcard.LastThumbprint);
        Assert.Equal(CertificateEnrollmentState.Synced, wildcard.State);
    }

    [Fact]
    public async Task Sync_ExistingSameThumbprint_SkipsImportStillSynced()
    {
        using var pfx = ArrangePfxDownload();
        _store.Setup(s => s.GetByIdAsync("enroll-1")).ReturnsAsync(Enrollment);
        _apiManagement.Setup(a => a.GetCertificatesAsync()).ReturnsAsync(new[]
        {
            new CertificateConfig { DomainName = "example.com", Thumbprint = pfx.Thumbprint },
        });

        var ok = await CreateService().SyncAsync("enroll-1", CancellationToken.None);

        Assert.True(ok, Enrollment.LastError);
        _apiManagement.Verify(a => a.CreateCertificateAsync(It.IsAny<CertificateConfig>()), Times.Never);
        Assert.Equal(CertificateEnrollmentState.Synced, Enrollment.State);
    }

    [Fact]
    public async Task Sync_CorruptPfx_MarksFailed()
    {
        _store.Setup(s => s.GetByIdAsync("enroll-1")).ReturnsAsync(Enrollment);
        _client.Setup(c => c.DownloadPfxAsync("example.com", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2, 3]);

        Assert.False(await CreateService().SyncAsync("enroll-1", CancellationToken.None));
        Assert.Equal(CertificateEnrollmentState.Failed, Enrollment.State);
        Assert.False(string.IsNullOrWhiteSpace(Enrollment.LastError));
    }

    // ── 事件处理 ──

    [Fact]
    public async Task HandleCompleted_UnknownDomain_IsIgnored()
    {
        _store.Setup(s => s.GetByDomainAsync("other.com")).ReturnsAsync((CertificateEnrollmentDto?)null);

        await CreateService().HandleCompletedAsync(Guid.NewGuid(), "other.com", CancellationToken.None);

        _store.Verify(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<CertificateEnrollmentDto>()), Times.Never);
    }

    [Fact]
    public async Task HandleCompleted_KnownDomain_SetsExternalIdAndSyncs()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _store.Setup(s => s.GetByDomainAsync("example.com")).ReturnsAsync(Enrollment);
        _store.Setup(s => s.GetByIdAsync("enroll-1")).ReturnsAsync(Enrollment);
        _client.Setup(c => c.DownloadPfxAsync("example.com", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        await CreateService().HandleCompletedAsync(id, "Example.COM", CancellationToken.None);

        Assert.Equal(id, Enrollment.ExternalCertificateId);
        _store.Verify(s => s.UpdateAsync("enroll-1", It.IsAny<CertificateEnrollmentDto>()), Times.Once);
    }

    [Fact]
    public async Task HandleFailed_KnownDomain_MarksFailed()
    {
        _store.Setup(s => s.GetByDomainAsync("example.com")).ReturnsAsync(Enrollment);

        await CreateService().HandleFailedAsync("example.com", "验证失败", CancellationToken.None);

        Assert.Equal(CertificateEnrollmentState.Failed, Enrollment.State);
        Assert.Equal("验证失败", Enrollment.LastError);
    }

    [Fact]
    public async Task HandleFailed_UnknownDomain_IsIgnored()
    {
        _store.Setup(s => s.GetByDomainAsync("other.com")).ReturnsAsync((CertificateEnrollmentDto?)null);

        await CreateService().HandleFailedAsync("other.com", "验证失败", CancellationToken.None);

        _store.Verify(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<CertificateEnrollmentDto>()), Times.Never);
    }

    // ── DeleteAsync ──

    [Fact]
    public async Task Delete_MissingEnrollment_ReturnsFalse()
    {
        _store.Setup(s => s.GetByIdAsync("nope")).ReturnsAsync((CertificateEnrollmentDto?)null);

        Assert.False(await CreateService().DeleteAsync("nope"));
        _store.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Delete_ExistingEnrollment_RemovesRegistration()
    {
        _store.Setup(s => s.GetByIdAsync("enroll-1")).ReturnsAsync(Enrollment);

        Assert.True(await CreateService().DeleteAsync("enroll-1"));
        _store.Verify(s => s.DeleteAsync("enroll-1"), Times.Once);
    }
}
