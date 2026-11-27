using Microsoft.Extensions.Logging.Abstractions;
using MinGo.Application.Services;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using Moq;

namespace MinGo.ControlPlane.Api.Tests;

public class RouteCertEnrollmentReconcileServiceTests
{
    private readonly Mock<ICertificateEnrollmentStore> _store = new();
    private readonly Mock<ICertManagerClient> _client = new();
    private readonly Mock<IApiManagementService> _apiManagement = new();
    private readonly Mock<ICertManagerSettingsService> _settings = new();

    private static RouteConfig Route(
        string? host,
        string? connectionId = "node:default",
        bool enabled = true) => new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = "r",
            ClusterId = "c",
            ConnectionId = connectionId!,
            Enabled = enabled,
            Match = new RouteMatch { Path = "/", Host = host },
        };

    private static CertManagerSettingsDto Settings(bool enabled = true, bool configured = true) => new()
    {
        BaseUrl = "http://certmanager.local",
        ApiKey = "abcd***",
        Enabled = enabled,
        Configured = configured,
    };

    private RouteCertEnrollmentReconcileService CreateService() => new(
        new CertificateEnrollmentService(
            _store.Object, _client.Object, _apiManagement.Object,
            NullLogger<CertificateEnrollmentService>.Instance),
        _store.Object,
        _apiManagement.Object,
        _settings.Object,
        NullLogger<RouteCertEnrollmentReconcileService>.Instance);

    private void ArrangeRoutes(params RouteConfig[] routes) =>
        _apiManagement.Setup(m => m.GetRoutesAsync()).ReturnsAsync(routes);

    /// <summary>按 RegisterAsync 的真实分支流（探测未命中 → 提交申请）编排 mock。</summary>
    private void ArrangeRegisterable(string domain)
    {
        var created = new CertificateEnrollmentDto
        {
            Id = "enroll-" + domain,
            Domain = domain,
            State = CertificateEnrollmentState.Unrequested,
        };
        _store.Setup(s => s.GetByDomainAsync(domain)).ReturnsAsync((CertificateEnrollmentDto?)null);
        _store.Setup(s => s.CreateAsync(It.IsAny<CertificateEnrollmentDto>())).ReturnsAsync(created);
        _store.Setup(s => s.GetByIdAsync(created.Id)).ReturnsAsync(created);
        _client.Setup(c => c.ProbeHasCertificateAsync(domain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _client.Setup(c => c.RequestCertificateAsync(
                domain, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task Reconcile_SettingsDisabled_SkipsEntirely()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings(enabled: false));
        ArrangeRoutes(Route("example.com"));

        await CreateService().ReconcileAsync(CancellationToken.None);

        _apiManagement.Verify(m => m.GetRoutesAsync(), Times.Never);
        _store.Verify(s => s.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task Reconcile_SettingsNotConfigured_SkipsEntirely()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings(configured: false));
        ArrangeRoutes(Route("example.com"));

        await CreateService().ReconcileAsync(CancellationToken.None);

        _apiManagement.Verify(m => m.GetRoutesAsync(), Times.Never);
        _store.Verify(s => s.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task Reconcile_NoDataPlaneHosts_DoesNotTouchStore()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings());
        ArrangeRoutes(Route("example.com", connectionId: "tunnel-1"));

        await CreateService().ReconcileAsync(CancellationToken.None);

        _store.Verify(s => s.GetAllAsync(), Times.Never);
        _store.Verify(s => s.CreateAsync(It.IsAny<CertificateEnrollmentDto>()), Times.Never);
    }

    [Fact]
    public async Task Reconcile_NewDomain_RegistersWithRequest()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings());
        ArrangeRoutes(Route("example.com"));
        _store.Setup(s => s.GetAllAsync()).ReturnsAsync(Array.Empty<CertificateEnrollmentDto>());
        ArrangeRegisterable("example.com");

        await CreateService().ReconcileAsync(CancellationToken.None);

        _store.Verify(s => s.CreateAsync(It.Is<CertificateEnrollmentDto>(
            d => d.Domain == "example.com" && !d.IsWildcard)), Times.Once);
        _client.Verify(c => c.RequestCertificateAsync(
            "example.com", false, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconcile_WildcardHost_RegistersAsWildcard()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings());
        ArrangeRoutes(Route("*.example.com"));
        _store.Setup(s => s.GetAllAsync()).ReturnsAsync(Array.Empty<CertificateEnrollmentDto>());
        ArrangeRegisterable("example.com");

        await CreateService().ReconcileAsync(CancellationToken.None);

        _store.Verify(s => s.CreateAsync(It.Is<CertificateEnrollmentDto>(
            d => d.Domain == "example.com" && d.IsWildcard)), Times.Once);
        _client.Verify(c => c.RequestCertificateAsync(
            "example.com", true, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconcile_ExistingEnabledEnrollment_IsNotTouched()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings());
        ArrangeRoutes(Route("example.com"));
        _store.Setup(s => s.GetAllAsync()).ReturnsAsync(new[]
        {
            new CertificateEnrollmentDto { Id = "e1", Domain = "example.com", Enabled = true },
        });

        await CreateService().ReconcileAsync(CancellationToken.None);

        _store.Verify(s => s.CreateAsync(It.IsAny<CertificateEnrollmentDto>()), Times.Never);
        _store.Verify(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<CertificateEnrollmentDto>()), Times.Never);
        _store.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Reconcile_WildcardDrift_UpgradesExistingEnrollment()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings());
        ArrangeRoutes(Route("*.example.com"));
        var existing = new CertificateEnrollmentDto
        {
            Id = "e1",
            Domain = "example.com",
            IsWildcard = false,
            Enabled = true,
        };
        _store.Setup(s => s.GetAllAsync()).ReturnsAsync(new[] { existing });

        await CreateService().ReconcileAsync(CancellationToken.None);

        Assert.True(existing.IsWildcard);
        _store.Verify(s => s.UpdateAsync("e1", It.Is<CertificateEnrollmentDto>(d => d.IsWildcard)), Times.Once);
        _store.Verify(s => s.CreateAsync(It.IsAny<CertificateEnrollmentDto>()), Times.Never);
    }

    [Fact]
    public async Task Reconcile_DisabledEnrollment_IsNeverReEnabledOrUpgraded()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings());
        ArrangeRoutes(Route("*.example.com"));
        _store.Setup(s => s.GetAllAsync()).ReturnsAsync(new[]
        {
            new CertificateEnrollmentDto
            {
                Id = "e1",
                Domain = "example.com",
                IsWildcard = false,
                Enabled = false,
            },
        });

        await CreateService().ReconcileAsync(CancellationToken.None);

        _store.Verify(s => s.CreateAsync(It.IsAny<CertificateEnrollmentDto>()), Times.Never);
        _store.Verify(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<CertificateEnrollmentDto>()), Times.Never);
        _store.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Reconcile_ConcurrentDuplicateRegistration_IsSwallowed()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings());
        ArrangeRoutes(Route("example.com"));
        _store.Setup(s => s.GetAllAsync()).ReturnsAsync(Array.Empty<CertificateEnrollmentDto>());
        // 对账读取后被人工登记抢先创建 → RegisterAsync 抛 EnrollmentException
        _store.Setup(s => s.GetByDomainAsync("example.com")).ReturnsAsync(
            new CertificateEnrollmentDto { Id = "e1", Domain = "example.com" });

        await CreateService().ReconcileAsync(CancellationToken.None);

        _store.Verify(s => s.CreateAsync(It.IsAny<CertificateEnrollmentDto>()), Times.Never);
    }

    [Fact]
    public async Task Reconcile_UiOnlyEnrollment_IsRetained()
    {
        _settings.Setup(s => s.GetAsync()).ReturnsAsync(Settings());
        ArrangeRoutes(Route("example.com"));
        _store.Setup(s => s.GetAllAsync()).ReturnsAsync(new[]
        {
            new CertificateEnrollmentDto { Id = "e1", Domain = "example.com", Enabled = true },
            new CertificateEnrollmentDto { Id = "e2", Domain = "manual-only.com", Enabled = true },
        });

        await CreateService().ReconcileAsync(CancellationToken.None);

        _store.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
        _store.Verify(s => s.CreateAsync(It.IsAny<CertificateEnrollmentDto>()), Times.Never);
    }
}