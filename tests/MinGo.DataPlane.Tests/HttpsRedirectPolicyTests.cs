using Microsoft.AspNetCore.Http;
using MinGo.DataPlane.Middleware;

namespace MinGo.DataPlane.Tests;

public class HttpsRedirectPolicyTests
{
    private static readonly HttpsRedirectOptions Enabled = new()
    {
        Enabled = true,
        Port = 8443
    };

    private static readonly MinGo.DataPlane.Kestrel.DataPlaneCertificateSelector Selector =
        TestCertificateFactory.CreateSelector(
            TestCertificateFactory.Create("api.example.com"),
            TestCertificateFactory.Create("expired.example.net", expired: true),
            TestCertificateFactory.Create("*.wild.example.com"));

    private static bool NeedsRedirect(
        string path = "/ping",
        string host = "api.example.com",
        string scheme = "http",
        HttpsRedirectOptions? options = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = string.IsNullOrEmpty(host) ? new HostString() : new HostString(host);
        context.Request.Path = path;

        return HttpsRedirectPolicy.NeedsRedirect(context, options ?? Enabled, Selector);
    }

    [Fact]
    public void CertHost_IsRedirected() => Assert.True(NeedsRedirect());

    [Fact]
    public void CertHost_WithPort_IsRedirected() => Assert.True(NeedsRedirect(host: "api.example.com:8080"));

    [Fact]
    public void CertHost_MixedCase_IsRedirected() => Assert.True(NeedsRedirect(host: "API.Example.COM"));

    [Fact]
    public void WildcardCertificate_RedirectsSubdomain() => Assert.True(NeedsRedirect(host: "shop.wild.example.com"));

    [Fact]
    public void ExpiredCertificate_StillRedirects() => Assert.True(NeedsRedirect(host: "expired.example.net"));

    [Fact]
    public void HostWithoutCertificate_IsNotRedirected() => Assert.False(NeedsRedirect(host: "unknown.example.com"));

    [Fact]
    public void WildcardCertificate_DoesNotRedirectApex() => Assert.False(NeedsRedirect(host: "wild.example.com"));

    [Fact]
    public void HttpOnlyHost_IsNotRedirected()
    {
        var options = new HttpsRedirectOptions
        {
            Enabled = true,
            Port = 8443,
            HttpOnlyHosts = ["api.example.com"]
        };

        Assert.False(NeedsRedirect(options: options));
    }

    [Fact]
    public void HttpOnlyWildcard_IsNotRedirected()
    {
        var options = new HttpsRedirectOptions
        {
            Enabled = true,
            Port = 8443,
            HttpOnlyHosts = ["*.wild.example.com"]
        };

        Assert.False(NeedsRedirect(host: "shop.wild.example.com", options: options));
    }

    [Fact]
    public void Localhost_IsNotRedirected() => Assert.False(NeedsRedirect(host: "localhost"));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.10")]
    [InlineData("::1")]
    public void IpLiteral_IsNotRedirected(string host) => Assert.False(NeedsRedirect(host: host));

    [Fact]
    public void EmptyHost_IsNotRedirected() => Assert.False(NeedsRedirect(host: ""));

    [Fact]
    public void HttpsRequest_IsNotRedirected() => Assert.False(NeedsRedirect(scheme: "https"));

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/healthz/live")]
    [InlineData("/healthz/ready")]
    public void HealthPath_IsNotRedirected(string path) => Assert.False(NeedsRedirect(path: path));

    [Fact]
    public void PathPrefix_DoesNotSwallowLookalike() => Assert.True(NeedsRedirect(path: "/healthzfoo"));

    [Fact]
    public void Disabled_AllowsEverything()
    {
        var options = new HttpsRedirectOptions { Enabled = false };

        Assert.False(NeedsRedirect(options: options));
    }
}

public class HttpsRedirectOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void EnabledWithInvalidPort_Throws(int port)
    {
        var options = new HttpsRedirectOptions { Enabled = true, Port = port };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void DisabledWithInvalidPort_DoesNotThrow()
    {
        var options = new HttpsRedirectOptions { Enabled = false, Port = 0 };

        options.Validate();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8443)]
    [InlineData(65535)]
    public void EnabledWithValidPort_DoesNotThrow(int port)
    {
        var options = new HttpsRedirectOptions { Enabled = true, Port = port };

        options.Validate();
    }
}
