namespace MinGo.Core.Portainer;

/// <summary>
/// 容器端口绑定。数据来自 Docker API 的 Ports 数组，仅保留 tcp 端口。
/// </summary>
public sealed record PortBinding(int PrivatePort, int? PublicPort, string? BindIp, string Protocol)
{
    public bool IsTcp => string.Equals(Protocol, "tcp", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 与 Portainer 响应解耦的容器快照，供发现领域的纯函数使用。
/// </summary>
public sealed record ContainerSnapshot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string State { get; init; }
    public required string Image { get; init; }
    public string? Stack { get; init; }
    public string? Service { get; init; }
    public IReadOnlyList<string> Networks { get; init; } = [];
    public IReadOnlyList<PortBinding> Ports { get; init; } = [];
    public bool IsPortainerSystem { get; init; }

    public bool IsRunning => string.Equals(State, "running", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 一条端口可用的地址候选；null 表示该形态不可用。
/// </summary>
public sealed record AddressCandidates(string? ContainerName, string? Host);

/// <summary>
/// 目标网络上下文：用于判断容器名形态是否可用。
/// </summary>
public sealed record NetworkContext(string Name, string? Driver, bool DataPlaneAttached)
{
    public bool IsDefaultBridge =>
        string.Equals(Name, DestinationAddressResolver.DefaultBridgeName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 自身 stack 识别结果。Source 取值：config | hostname | image | none。
/// </summary>
public sealed record SelfIdentity(
    bool Detected,
    string Source,
    IReadOnlyList<string> Stacks,
    int ContainerCount)
{
    public static readonly SelfIdentity None = new(false, "none", [], 0);

    public bool IsSelfStack(string? stack) =>
        stack is not null &&
        Stacks.Any(s => string.Equals(s, stack, StringComparison.OrdinalIgnoreCase));
}

public sealed record ExcludedContainer(
    string Name,
    string Reason,
    string? Stack = null,
    string? Endpoint = null);

public sealed record ExclusionResult(
    IReadOnlyList<ContainerSnapshot> Included,
    IReadOnlyList<ExcludedContainer> Excluded);
