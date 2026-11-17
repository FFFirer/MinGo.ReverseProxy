using MinGo.ControlPlane.Api.Configuration;

namespace MinGo.ControlPlane.Api.Tests;

/// <summary>
/// 隧道级回源地址解析测试：回源地址必须由 cloudflared 所在网络可达，
/// 因此按隧道名覆盖优先、全局默认兜底。
/// </summary>
public class CloudflareOptionsTests
{
    private const string DefaultOrigin = "http://data-plane:8080";

    private static CloudflareOptions OptionsWith(params (string Name, string Origin)[] overrides) => new()
    {
        DataPlaneOrigin = DefaultOrigin,
        TunnelOrigins = overrides
            .Select(o => new CloudflareTunnelOrigin { TunnelName = o.Name, Origin = o.Origin })
            .ToList(),
    };

    [Fact]
    public void ResolveOrigin_UsesGlobalDefault_WhenNoOverridesConfigured()
    {
        var options = new CloudflareOptions { DataPlaneOrigin = DefaultOrigin };

        var resolved = options.ResolveOrigin("home");

        Assert.Equal(DefaultOrigin, resolved.Origin);
        Assert.Equal(CloudflareOptions.OriginSourceDefault, resolved.Source);
    }

    [Fact]
    public void ResolveOrigin_UsesOverride_WhenTunnelNameMatches()
    {
        var options = OptionsWith(("home", "http://192.168.88.134:8080"));

        var resolved = options.ResolveOrigin("home");

        Assert.Equal("http://192.168.88.134:8080", resolved.Origin);
        Assert.Equal(CloudflareOptions.OriginSourceTunnelOverride, resolved.Source);
    }

    [Fact]
    public void ResolveOrigin_MatchesTunnelName_CaseInsensitivelyAndIgnoringWhitespace()
    {
        var options = OptionsWith(("Home-NAS", "http://100.94.195.53:8080"));

        var resolved = options.ResolveOrigin("  home-nas ");

        Assert.Equal("http://100.94.195.53:8080", resolved.Origin);
        Assert.Equal(CloudflareOptions.OriginSourceTunnelOverride, resolved.Source);
    }

    [Fact]
    public void ResolveOrigin_FallsBackToDefault_WhenTunnelNameMisses()
    {
        var options = OptionsWith(("home", "http://192.168.88.134:8080"));

        var resolved = options.ResolveOrigin("vps");

        Assert.Equal(DefaultOrigin, resolved.Origin);
        Assert.Equal(CloudflareOptions.OriginSourceDefault, resolved.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveOrigin_FallsBackToDefault_WhenTunnelNameMissing(string? tunnelName)
    {
        var options = OptionsWith(("home", "http://192.168.88.134:8080"));

        Assert.Equal(DefaultOrigin, options.ResolveOrigin(tunnelName).Origin);
    }

    [Fact]
    public void ResolveOrigin_IgnoresIncompleteOverrideEntries()
    {
        // 写了一半的覆盖项不应把地址变成空串，让隧道彻底无法回源
        var options = new CloudflareOptions
        {
            DataPlaneOrigin = DefaultOrigin,
            TunnelOrigins =
            [
                new CloudflareTunnelOrigin { TunnelName = "blank-origin", Origin = "" },
                new CloudflareTunnelOrigin { TunnelName = "  ", Origin = "http://x:1" },
            ],
        };

        Assert.Equal(DefaultOrigin, options.ResolveOrigin("blank-origin").Origin);
        Assert.Equal(DefaultOrigin, options.ResolveOrigin("blank-name").Origin);
    }

    [Fact]
    public void ResolveOrigin_TrimsSurroundingWhitespaceFromOverrideValue()
    {
        var options = OptionsWith(("home", "  http://192.168.88.134:8080  "));

        Assert.Equal("http://192.168.88.134:8080", options.ResolveOrigin("home").Origin);
    }

    [Fact]
    public void ResolveOrigin_ToleratesNullOverrideList()
    {
        var options = new CloudflareOptions { DataPlaneOrigin = DefaultOrigin, TunnelOrigins = null! };

        Assert.Equal(DefaultOrigin, options.ResolveOrigin("home").Origin);
    }
}
