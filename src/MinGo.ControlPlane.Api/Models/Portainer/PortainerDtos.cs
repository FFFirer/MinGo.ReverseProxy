using MinGo.Core.Portainer;

namespace MinGo.ControlPlane.Api.Models.Portainer;

// 前端消费的响应模型。ApiKey 永不出现在任何响应中。

public sealed record PortainerStatusResponse(
    string InstanceId,
    bool Enabled,
    string? BaseUrl,
    bool Reachable,
    string? Version,
    int? EndpointCount,
    bool ApiKeyConfigured,
    PortainerSelfIdentity SelfDetection,
    string? Error);

public sealed record PortainerSelfIdentity(
    bool Detected,
    string Source,
    IReadOnlyList<string> Stacks,
    int ContainerCount)
{
    public static PortainerSelfIdentity Empty { get; } = new(false, SelfIdentityResolver.SourceNone, [], 0);

    public static PortainerSelfIdentity From(SelfIdentity self) =>
        new(self.Detected, self.Source, self.Stacks, self.ContainerCount);
}

public sealed record PortainerDiscoveryResponse(
    string InstanceId,
    bool Connected,
    string? Version,
    PortainerSelfIdentity Self,
    IReadOnlyList<PortainerEndpointView> Endpoints,
    IReadOnlyList<PortainerExcludedView> Excluded);

public sealed record PortainerEndpointView(
    int Id,
    string Name,
    string Engine,
    string Status,
    string? Error,
    IReadOnlyList<PortainerNetworkView> Networks);

public sealed record PortainerNetworkView(
    string Name,
    string? Driver,
    bool IsDefaultBridge,
    bool DataPlaneAttached,
    bool SupportsContainerName,
    IReadOnlyList<PortainerGroupView> Groups);

public sealed record PortainerGroupView(
    string Name,
    string Source,
    IReadOnlyList<PortainerContainerView> Containers);

public sealed record PortainerContainerView(
    string Id,
    string Name,
    string Image,
    string? Service,
    IReadOnlyList<PortainerPortView> Ports);

public sealed record PortainerPortView(
    int Private,
    int? Public,
    string? BindIp,
    string Protocol,
    PortainerAddressView Addresses);

public sealed record PortainerAddressView(string? ContainerName, string? Host);

public sealed record PortainerExcludedView(
    string Name,
    string Reason,
    string? Stack,
    string? Endpoint);
