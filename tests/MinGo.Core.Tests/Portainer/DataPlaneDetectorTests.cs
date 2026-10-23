using MinGo.Core.Portainer;

namespace MinGo.Core.Tests.Portainer;

public class DataPlaneDetectorTests
{
    private static readonly SelfIdentity Self =
        new(true, SelfIdentityResolver.SourceHostname, ["reverseproxy"], 2);

    [Fact]
    public void IsAttached_WhenSelfDataPlaneSharesNetwork_ReturnsTrue()
    {
        // debian13 EP4：reverseproxy-data-plane-1 与业务容器同在 default_bridge。
        Assert.True(DataPlaneDetector.IsAttached(
            [Snap.SelfDataPlane(), Snap.StaticPages()],
            "default_bridge",
            Self,
            "data-plane"));
    }

    [Fact]
    public void IsAttached_WhenDataPlaneIsOnAnotherNetwork_ReturnsFalse()
    {
        var isolatedDataPlane = Snap.Container(
            "dp000000000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "mingo-reverseproxy-data-plane-1",
            stack: "mingo-reverseproxy",
            service: "data-plane",
            networks: ["mingo-reverseproxy_default"]);

        Assert.False(DataPlaneDetector.IsAttached(
            [isolatedDataPlane, Snap.StaticPages()],
            "bridge-local",
            Self,
            "data-plane"));
    }

    [Fact]
    public void IsAttached_IgnoresOtherStacksDataPlane()
    {
        var otherDataPlane = Snap.Container(
            "dp000000000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "other-data-plane-1",
            stack: "other-stack",
            service: "data-plane",
            networks: ["default_bridge"]);

        Assert.False(DataPlaneDetector.IsAttached(
            [otherDataPlane],
            "default_bridge",
            Self,
            "data-plane"));
    }

    [Fact]
    public void IsAttached_IgnoresStoppedDataPlane()
    {
        var stopped = Snap.Container(
            "dp000000000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "reverseproxy-data-plane-1",
            state: "exited",
            stack: "reverseproxy",
            service: "data-plane",
            networks: ["default_bridge"]);

        Assert.False(DataPlaneDetector.IsAttached(
            [stopped],
            "default_bridge",
            Self,
            "data-plane"));
    }

    [Fact]
    public void IsAttached_WhenSelfUnknown_ReturnsFalse()
    {
        Assert.False(DataPlaneDetector.IsAttached(
            [Snap.SelfDataPlane()],
            "default_bridge",
            SelfIdentity.None,
            "data-plane"));
    }

    [Fact]
    public void IsAttached_UsesConfiguredServiceName()
    {
        var custom = Snap.Container(
            "dp000000000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "reverseproxy-dp-1",
            stack: "reverseproxy",
            service: "dp",
            networks: ["default_bridge"]);

        Assert.True(DataPlaneDetector.IsAttached(
            [custom],
            "default_bridge",
            Self,
            "dp"));
    }
}
