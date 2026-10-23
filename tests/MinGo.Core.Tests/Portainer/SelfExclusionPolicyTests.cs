using MinGo.Core.Portainer;

namespace MinGo.Core.Tests.Portainer;

public class SelfExclusionPolicyTests
{
    private static readonly SelfIdentity Self =
        new(true, SelfIdentityResolver.SourceHostname, ["reverseproxy"], 3);

    [Fact]
    public void Apply_ExcludesSelfStack_PortainerSystem_Stopped_AndExtra()
    {
        var selfControlPlane = Snap.SelfControlPlane();
        var portainerAgent = Snap.Container(
            "agent0000000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "portainer-agent",
            stack: "portainer-agent",
            service: "agent",
            portainerSystem: true);
        var exitedMigration = Snap.Container(
            "migrate00000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "reverseproxy-migrate-1",
            state: "exited",
            stack: "reverseproxy",
            service: "migrate");
        var legacy = Snap.Container(
            "legacy000000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "1-control-plane-1",
            stack: "1",
            service: "control-plane");
        var app = Snap.StaticPages();

        var result = SelfExclusionPolicy.Apply(
            [selfControlPlane, portainerAgent, exitedMigration, legacy, app],
            Self,
            extraExcludedStacks: ["1"],
            endpoint: "rootless");

        Assert.Equal(["static-pages"], result.Included.Select(c => c.Name));
        Assert.Equal(4, result.Excluded.Count);

        Assert.Equal(
            SelfExclusionPolicy.ReasonSelfStack,
            result.Excluded.Single(e => e.Name == "reverseproxy-control-plane-1").Reason);
        Assert.Equal(
            SelfExclusionPolicy.ReasonPortainerSystem,
            result.Excluded.Single(e => e.Name == "portainer-agent").Reason);
        Assert.Equal(
            SelfExclusionPolicy.ReasonNotRunning,
            result.Excluded.Single(e => e.Name == "reverseproxy-migrate-1").Reason);
        Assert.Equal(
            SelfExclusionPolicy.ReasonExtraExcluded,
            result.Excluded.Single(e => e.Name == "1-control-plane-1").Reason);

        Assert.All(result.Excluded, e => Assert.Equal("rootless", e.Endpoint));
    }

    [Fact]
    public void Apply_WhenSelfNotDetected_DoesNotExcludeAnyStackBySelfRule()
    {
        var result = SelfExclusionPolicy.Apply(
            [Snap.SelfControlPlane(), Snap.StaticPages()],
            SelfIdentity.None,
            extraExcludedStacks: []);

        Assert.Equal(2, result.Included.Count);
        Assert.Empty(result.Excluded);
    }

    [Fact]
    public void Apply_Precedence_StoppedPortainerSystemContainerIsNotRunning()
    {
        var container = Snap.Container(
            "stopped00000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "portainer",
            state: "exited",
            stack: "portainer",
            portainerSystem: true);

        var result = SelfExclusionPolicy.Apply([container], Self, extraExcludedStacks: null);

        Assert.Empty(result.Included);
        Assert.Equal(SelfExclusionPolicy.ReasonNotRunning, Assert.Single(result.Excluded).Reason);
    }

    [Fact]
    public void Apply_ExtraExcludedStack_IsCaseInsensitiveAndTrimmed()
    {
        var legacy = Snap.Container(
            "legacy000000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "1-control-plane-1",
            stack: "1",
            service: "control-plane");

        var result = SelfExclusionPolicy.Apply(
            [legacy],
            Self,
            extraExcludedStacks: ["  1  "]);

        Assert.Equal(SelfExclusionPolicy.ReasonExtraExcluded, Assert.Single(result.Excluded).Reason);
    }
}
