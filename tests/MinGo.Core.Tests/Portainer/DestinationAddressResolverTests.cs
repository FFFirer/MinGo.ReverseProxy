using MinGo.Core.Portainer;

namespace MinGo.Core.Tests.Portainer;

public class DestinationAddressResolverTests
{
    private const string HostAddress = "192.168.88.134";

    private static readonly ContainerSnapshot StaticPages = Snap.StaticPages();

    [Fact]
    public void Resolve_UserDefinedNetworkWithDataPlane_OffersBothForms()
    {
        var network = new NetworkContext("default_bridge", "bridge", DataPlaneAttached: true);
        var port = Snap.Port(8080, 5218);

        var addresses = DestinationAddressResolver.Resolve(StaticPages, port, network, HostAddress);

        Assert.Equal("http://static-pages:8080", addresses.ContainerName);
        Assert.Equal("http://192.168.88.134:5218", addresses.Host);
    }

    [Fact]
    public void Resolve_DefaultBridge_DisablesContainerName()
    {
        var network = new NetworkContext("bridge", "bridge", DataPlaneAttached: true);

        var addresses = DestinationAddressResolver.Resolve(
            StaticPages, Snap.Port(8080, 5218), network, HostAddress);

        Assert.Null(addresses.ContainerName);
        Assert.Equal("http://192.168.88.134:5218", addresses.Host);
    }

    [Fact]
    public void Resolve_DataPlaneNotAttached_DisablesContainerName()
    {
        var network = new NetworkContext("bridge-local", "bridge", DataPlaneAttached: false);

        var addresses = DestinationAddressResolver.Resolve(
            StaticPages, Snap.Port(8080, 5218), network, HostAddress);

        Assert.Null(addresses.ContainerName);
        Assert.Equal("http://192.168.88.134:5218", addresses.Host);
    }

    [Theory]
    [InlineData("host", false)]
    [InlineData("none", false)]
    [InlineData("macvlan", false)]
    [InlineData("ipvlan", false)]
    [InlineData("bridge", true)]
    [InlineData("overlay", true)]
    [InlineData(null, true)]
    public void SupportsContainerName_RequiresUserDefinedBridgeOrOverlay(string? driver, bool expected)
    {
        Assert.Equal(expected, DestinationAddressResolver.SupportsContainerName("some-net", driver));
    }

    [Theory]
    [InlineData("bridge")]
    [InlineData("BRIDGE")]
    [InlineData(null)]
    [InlineData("")]
    public void SupportsContainerName_DefaultBridgeOrEmpty_IsFalse(string? name)
    {
        Assert.False(DestinationAddressResolver.SupportsContainerName(name, "bridge"));
    }

    [Fact]
    public void Resolve_ConcreteBindIp_TakesPrecedenceOverConfiguredHost()
    {
        // debian13 adguardhome：53 端口绑定在 192.168.88.134，而不是 0.0.0.0。
        var port = Snap.Port(53, 53, bindIp: "192.168.88.134");
        var network = new NetworkContext("adguardhome_dns-net", "bridge", DataPlaneAttached: false);

        var addresses = DestinationAddressResolver.Resolve(
            StaticPages, port, network, configuredHostAddress: "100.94.195.53");

        Assert.Equal("http://192.168.88.134:53", addresses.Host);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("[::]")]
    [InlineData("*")]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_WildcardBind_UsesConfiguredHost(string? bindIp)
    {
        var addresses = DestinationAddressResolver.Resolve(
            StaticPages,
            Snap.Port(8080, 5218, bindIp),
            new NetworkContext("default_bridge", "bridge", true),
            HostAddress);

        Assert.Equal("http://192.168.88.134:5218", addresses.Host);
    }

    [Fact]
    public void Resolve_WildcardBindWithoutConfiguredHost_LeavesHostEmpty()
    {
        var addresses = DestinationAddressResolver.Resolve(
            StaticPages,
            Snap.Port(8080, 5218, "0.0.0.0"),
            new NetworkContext("default_bridge", "bridge", false),
            configuredHostAddress: null);

        Assert.Null(addresses.Host);
    }

    [Fact]
    public void Resolve_UnpublishedPort_OnlyOffersContainerName()
    {
        var addresses = DestinationAddressResolver.Resolve(
            StaticPages,
            Snap.Port(8080),
            new NetworkContext("default_bridge", "bridge", true),
            HostAddress);

        Assert.Equal("http://static-pages:8080", addresses.ContainerName);
        Assert.Null(addresses.Host);
    }

    [Fact]
    public void Resolve_ConfiguredHostWithScheme_ExtractsHost()
    {
        var addresses = DestinationAddressResolver.Resolve(
            StaticPages,
            Snap.Port(8080, 5218, "0.0.0.0"),
            new NetworkContext("default_bridge", "bridge", false),
            "http://gateway.example.com:9000/");

        Assert.Equal("http://gateway.example.com:5218", addresses.Host);
    }

    [Fact]
    public void Resolve_ConcreteIpv6Bind_IsBracketed()
    {
        var addresses = DestinationAddressResolver.Resolve(
            StaticPages,
            Snap.Port(8080, 5218, "fd00::10"),
            new NetworkContext("default_bridge", "bridge", false),
            HostAddress);

        Assert.Equal("http://[fd00::10]:5218", addresses.Host);
    }

    [Fact]
    public void Resolve_ConfiguredIpv6Host_IsBracketed()
    {
        var addresses = DestinationAddressResolver.Resolve(
            StaticPages,
            Snap.Port(8080, 5218, "0.0.0.0"),
            new NetworkContext("default_bridge", "bridge", false),
            "fd00::20");

        Assert.Equal("http://[fd00::20]:5218", addresses.Host);
    }

    [Fact]
    public void Resolve_NeverProducesContainerIpForm()
    {
        // 容器 IP 形态在两个 Podman daemon 间有歧义（10.89.1.4 重复），必须永不出现。
        var addresses = DestinationAddressResolver.Resolve(
            StaticPages,
            Snap.Port(8080, 5218, "0.0.0.0"),
            new NetworkContext("default_bridge", "bridge", true),
            HostAddress);

        Assert.DoesNotContain("10.89.1.12", addresses.ContainerName);
        Assert.DoesNotContain("10.89.1.12", addresses.Host);
    }
}
