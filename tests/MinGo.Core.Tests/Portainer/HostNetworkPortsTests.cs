using MinGo.Core.Portainer;

namespace MinGo.Core.Tests.Portainer;

public class HostNetworkPortsTests
{
    [Theory]
    [InlineData("host", true)]
    [InlineData("HOST", true)]
    [InlineData(" host ", true)]
    [InlineData("default", false)]
    [InlineData("bridge", false)]
    [InlineData("container:abc123", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsHostMode_MatchesOnlyHost(string? networkMode, bool expected)
    {
        Assert.Equal(expected, HostNetworkPorts.IsHostMode(networkMode));
    }

    [Theory]
    [InlineData("host", true)]
    [InlineData("Host", true)]
    [InlineData("bridge", false)]
    [InlineData(null, false)]
    public void IsHostNetwork_MatchesByName(string? networkName, bool expected)
    {
        Assert.Equal(expected, HostNetworkPorts.IsHostNetwork(networkName));
    }

    [Fact]
    public void FromExposedPorts_KeepsOnlyTcp_AndMirrorsPortNumbers()
    {
        var ports = HostNetworkPorts.FromExposedPorts(["80/tcp", "443/udp", "8080/tcp", "53/udp"]);

        Assert.Equal(2, ports.Count);
        Assert.Equal(new PortBinding(80, 80, null, "tcp"), ports[0]);
        Assert.Equal(new PortBinding(8080, 8080, null, "tcp"), ports[1]);
    }

    [Fact]
    public void FromExposedPorts_SkipsMalformedEntries()
    {
        var ports = HostNetworkPorts.FromExposedPorts(["80/tcp", "abc", "", "0/tcp", "8080"]);

        Assert.Single(ports);
        Assert.Equal(80, ports[0].PrivatePort);
    }

    [Fact]
    public void FromExposedPorts_EmptyOrNull_YieldsEmpty()
    {
        Assert.Empty(HostNetworkPorts.FromExposedPorts(null));
        Assert.Empty(HostNetworkPorts.FromExposedPorts([]));
    }

    [Fact]
    public void Resolve_HostNetwork_OnlyOffersHostAddress()
    {
        var container = Snap.Container(
            "c1", "hosted-service", networks: ["host"], ports: [Snap.Port(80, 80)]);
        var network = new NetworkContext("host", "host", DataPlaneAttached: true);

        var addresses = DestinationAddressResolver.Resolve(
            container, Snap.Port(80, 80), network, "192.168.88.134");

        Assert.Null(addresses.ContainerName);
        Assert.Equal("http://192.168.88.134:80", addresses.Host);
    }
}
