using MinGo.Core.Models;

namespace MinGo.Core.Tests.Certificates;

public class CertificateEnrollmentPolicyTests
{
    [Theory]
    [InlineData("Example.COM", "example.com")]
    [InlineData("  example.com  ", "example.com")]
    public void NormalizeDomain_TrimsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, CertificateEnrollmentPolicy.NormalizeDomain(input));
    }

    [Fact]
    public void GetImportDomains_SingleDomain_ReturnsBareDomainOnly()
    {
        var domains = CertificateEnrollmentPolicy.GetImportDomains("example.com", isWildcard: false);

        Assert.Equal(new[] { "example.com" }, domains);
    }

    [Fact]
    public void GetImportDomains_Wildcard_ReturnsBareAndWildcardRows()
    {
        var domains = CertificateEnrollmentPolicy.GetImportDomains("Example.COM", isWildcard: true);

        Assert.Equal(new[] { "example.com", "*.example.com" }, domains);
    }

    [Fact]
    public void FilterNewDomains_SameDomainSameThumbprint_IsSkipped()
    {
        var existing = new[]
        {
            new CertificateConfig { DomainName = "example.com", Thumbprint = "ABCDEF" },
        };

        var result = CertificateEnrollmentPolicy.FilterNewDomains(
            existing, ["example.com"], "abcdef");

        Assert.Empty(result);
    }

    [Fact]
    public void FilterNewDomains_Wildcard_SkipsOnlyMatchingRows()
    {
        var existing = new[]
        {
            new CertificateConfig { DomainName = "example.com", Thumbprint = "ABC" },
        };

        // 裸域已存在同指纹 → 跳过；*.example.com 不存在 → 保留
        var result = CertificateEnrollmentPolicy.FilterNewDomains(
            existing, ["example.com", "*.example.com"], "ABC");

        Assert.Equal(new[] { "*.example.com" }, result);
    }

    [Fact]
    public void FilterNewDomains_DifferentThumbprint_IsKept()
    {
        var existing = new[]
        {
            new CertificateConfig { DomainName = "example.com", Thumbprint = "OLD" },
        };

        var result = CertificateEnrollmentPolicy.FilterNewDomains(
            existing, ["example.com"], "NEW");

        Assert.Equal(new[] { "example.com" }, result);
    }

    [Fact]
    public void FilterNewDomains_ExistingWithoutThumbprint_IsIgnored()
    {
        var existing = new[]
        {
            new CertificateConfig { DomainName = "example.com", Thumbprint = "" },
        };

        var result = CertificateEnrollmentPolicy.FilterNewDomains(
            existing, ["example.com"], "ABC");

        Assert.Equal(new[] { "example.com" }, result);
    }

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("a.b.example.com", true)]
    [InlineData("Example.COM", true)]
    [InlineData("example", false)]            // 单标签
    [InlineData("", false)]
    [InlineData("https://example.com", false)]
    [InlineData("example.com:443", false)]
    [InlineData("*.example.com", false)]      // 泛域名用开关表示
    [InlineData("example.com/path", false)]
    [InlineData("exa_mple.com", false)]
    public void IsValidDomain_EnforcesBareDomainRules(string input, bool expected)
    {
        Assert.Equal(expected, CertificateEnrollmentPolicy.IsValidDomain(input));
    }
}
