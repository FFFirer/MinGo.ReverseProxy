using MinGo.Core.Models;

namespace MinGo.Core.Tests.Certificates;

public class CertificateSnapshotPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static CertificateConfig Cert(
        string domain, DateTimeOffset? expiresAt, string? id = null, byte[]? data = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString(),
        DomainName = domain,
        ExpiresAt = expiresAt,
        CertificateData = data ?? new byte[] { 1, 2, 3 }
    };

    [Fact]
    public void RecordsWithoutBytes_AreDropped()
    {
        var result = CertificateSnapshotPolicy.SelectForDelivery(
            new[] { Cert("empty.local", Now.AddDays(30), data: Array.Empty<byte>()), Cert("ok.local", Now.AddDays(30)) },
            Now);

        Assert.Equal("ok.local", Assert.Single(result).DomainName);
    }

    [Fact]
    public void RecordsWithoutDomain_AreDropped()
    {
        var result = CertificateSnapshotPolicy.SelectForDelivery(
            new[] { Cert("  ", Now.AddDays(30)), Cert("ok.local", Now.AddDays(30)) },
            Now);

        Assert.Equal("ok.local", Assert.Single(result).DomainName);
    }

    [Fact]
    public void ExpiredCertificate_IsStillDelivered()
    {
        var expired = Cert("old.local", Now.AddDays(-10));

        var result = CertificateSnapshotPolicy.SelectForDelivery(new[] { expired }, Now);

        var delivered = Assert.Single(result);
        Assert.Equal("old.local", delivered.DomainName);
        Assert.Equal(expired.ExpiresAt, delivered.ExpiresAt);
    }

    [Fact]
    public void SameDomain_PrefersUnexpiredOverExpired()
    {
        var expired = Cert("site.local", Now.AddDays(-1), id: "a");
        var fresh = Cert("site.local", Now.AddDays(10), id: "b");

        var result = CertificateSnapshotPolicy.SelectForDelivery(new[] { expired, fresh }, Now);

        Assert.Equal("b", Assert.Single(result).Id);
    }

    [Fact]
    public void SameDomain_PicksLatestExpiry()
    {
        var early = Cert("site.local", Now.AddDays(5), id: "a");
        var late = Cert("site.local", Now.AddDays(50), id: "b");

        var result = CertificateSnapshotPolicy.SelectForDelivery(new[] { late, early }, Now);

        Assert.Equal("b", Assert.Single(result).Id);
    }

    [Fact]
    public void SameDomain_AllExpired_PicksLatestExpiry()
    {
        var older = Cert("site.local", Now.AddDays(-30), id: "a");
        var newer = Cert("site.local", Now.AddDays(-1), id: "b");

        var result = CertificateSnapshotPolicy.SelectForDelivery(new[] { older, newer }, Now);

        Assert.Equal("b", Assert.Single(result).Id);
    }

    [Fact]
    public void SameDomain_NullExpiryCountsAsLongestLived()
    {
        var dated = Cert("site.local", Now.AddDays(365), id: "a");
        var permanent = Cert("site.local", null, id: "b");

        var result = CertificateSnapshotPolicy.SelectForDelivery(new[] { dated, permanent }, Now);

        Assert.Equal("b", Assert.Single(result).Id);
    }

    [Fact]
    public void SameDomain_ComparisonIsCaseInsensitive_AndOnlyOneIsDelivered()
    {
        var lower = Cert("Site.Local", Now.AddDays(10), id: "lower");

        var result = CertificateSnapshotPolicy.SelectForDelivery(
            new[] { lower, Cert("site.local", Now.AddDays(20), id: "upper") }, Now);

        Assert.Equal("upper", Assert.Single(result).Id);
    }

    [Fact]
    public void OutputOrder_IsDeterministic_RegardlessOfInputOrder()
    {
        var certs = new[]
        {
            Cert("zeta.local", Now.AddDays(10)),
            Cert("Alpha.Local", Now.AddDays(10)),
            Cert("beta.local", Now.AddDays(10))
        };

        var forward = CertificateSnapshotPolicy.SelectForDelivery(certs, Now).Select(c => c.DomainName).ToArray();
        var reversed = CertificateSnapshotPolicy.SelectForDelivery(certs.Reverse(), Now).Select(c => c.DomainName).ToArray();

        Assert.Equal(new[] { "Alpha.Local", "beta.local", "zeta.local" }, forward);
        Assert.Equal(forward, reversed);
    }
}
