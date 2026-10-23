using MinGo.Core.Portainer;

namespace MinGo.Core.Tests.Portainer;

public class SelfIdentityResolverTests
{
    private static readonly ContainerSnapshot DataPlane = Snap.SelfDataPlane();
    private static readonly ContainerSnapshot ControlPlane = Snap.SelfControlPlane();
    private static readonly ContainerSnapshot StaticPages = Snap.StaticPages();

    [Fact]
    public void Resolve_ConfigOverrideWins_EvenWhenHostnameWouldMatchOtherStack()
    {
        var other = Snap.Container(
            "999999999999ffffffffffffffffffffffffffffffffffffffffffffffffffff",
            "legacy-control-plane",
            image: "registry/1mingo/reverseproxy-cp:latest",
            stack: "1",
            service: "control-plane");

        var self = SelfIdentityResolver.Resolve(
            "d69dc8afd9d4",
            [ControlPlane, DataPlane, other],
            configuredStacks: ["mingo-reverseproxy"],
            imagePatterns: ["reverseproxy-cp"]);

        Assert.True(self.Detected);
        Assert.Equal(SelfIdentityResolver.SourceConfig, self.Source);
        Assert.Equal(["mingo-reverseproxy"], self.Stacks);
        Assert.Equal(0, self.ContainerCount);
    }

    [Fact]
    public void Resolve_HostnameMatchesContainerIdPrefix_DerivesComposeProject()
    {
        var self = SelfIdentityResolver.Resolve(
            "d69dc8afd9d4",
            [ControlPlane, DataPlane, StaticPages],
            configuredStacks: [],
            imagePatterns: ["reverseproxy-cp"]);

        Assert.True(self.Detected);
        Assert.Equal(SelfIdentityResolver.SourceHostname, self.Source);
        Assert.Equal(["reverseproxy"], self.Stacks);
        Assert.Equal(2, self.ContainerCount);
        Assert.True(self.IsSelfStack("reverseproxy"));
        Assert.False(self.IsSelfStack("staticpages"));
    }

    [Fact]
    public void Resolve_HostnameMiss_DoesNotExcludeSimilarlyNamedStacks()
    {
        // 控制面在 debian12，Portainer 指向 debian13：主机名匹配不上，
        // debian13 上另一个 reverseproxy 栈不能被误判为自身。
        var self = SelfIdentityResolver.Resolve(
            "11c3258b03d3",
            [ControlPlane, DataPlane, StaticPages],
            configuredStacks: [],
            imagePatterns: []);

        Assert.False(self.Detected);
        Assert.Equal(SelfIdentityResolver.SourceNone, self.Source);
        Assert.Empty(self.Stacks);
    }

    [Fact]
    public void Resolve_CustomHostname_FallsBackToImagePattern_OnlyRunningContainers()
    {
        var stoppedCp = Snap.Container(
            "bbbb00000000bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "1-control-plane-1",
            state: "created",
            image: "registry.private.fffirer.top:9081/1mingo/reverseproxy-cp:latest",
            stack: "1",
            service: "control-plane");

        var self = SelfIdentityResolver.Resolve(
            "reverseproxy-host",
            [ControlPlane, DataPlane, stoppedCp],
            configuredStacks: [],
            imagePatterns: ["reverseproxy-cp"]);

        Assert.True(self.Detected);
        Assert.Equal(SelfIdentityResolver.SourceImage, self.Source);
        Assert.Equal(["reverseproxy"], self.Stacks);
        Assert.Equal(2, self.ContainerCount);
    }

    [Fact]
    public void Resolve_NothingMatches_ReturnsNone()
    {
        var self = SelfIdentityResolver.Resolve(
            "reverseproxy-host",
            [StaticPages],
            configuredStacks: [],
            imagePatterns: ["reverseproxy-cp"]);

        Assert.False(self.Detected);
        Assert.Equal(SelfIdentityResolver.SourceNone, self.Source);
        Assert.Equal(0, self.ContainerCount);
    }

    [Fact]
    public void Resolve_ConfigStacksAreTrimmedAndDeduplicated()
    {
        var self = SelfIdentityResolver.Resolve(
            null,
            [ControlPlane],
            configuredStacks: [" reverseproxy ", "REVERSEPROXY", ""],
            imagePatterns: []);

        Assert.Equal(SelfIdentityResolver.SourceConfig, self.Source);
        Assert.Equal(["reverseproxy"], self.Stacks);
        Assert.Equal(1, self.ContainerCount);
    }
}
