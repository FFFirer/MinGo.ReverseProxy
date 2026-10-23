namespace MinGo.Core.Portainer;

/// <summary>
/// 为容器端口推导候选地址。
/// 仅提供两种形态：容器名 + 私有端口、宿主地址 + 发布端口；永不使用容器 IP。
/// </summary>
public static class DestinationAddressResolver
{
    public const string DefaultBridgeName = "bridge";

    private static readonly string[] WildcardBindAddresses = ["0.0.0.0", "::", "[::]", "*"];

    /// <summary>
    /// 容器名 DNS 解析只在 user-defined bridge/overlay 上成立，默认 bridge 不支持。
    /// </summary>
    public static bool SupportsContainerName(string? networkName, string? driver)
    {
        if (string.IsNullOrWhiteSpace(networkName)) return false;
        if (string.Equals(networkName.Trim(), DefaultBridgeName, StringComparison.OrdinalIgnoreCase)) return false;

        if (!string.IsNullOrWhiteSpace(driver))
        {
            var normalized = driver.Trim();
            if (!string.Equals(normalized, "bridge", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized, "overlay", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static AddressCandidates Resolve(
        ContainerSnapshot container,
        PortBinding port,
        NetworkContext network,
        string? configuredHostAddress)
    {
        string? containerAddress = null;
        if (network.DataPlaneAttached &&
            SupportsContainerName(network.Name, network.Driver) &&
            !string.IsNullOrWhiteSpace(container.Name))
        {
            containerAddress = $"http://{container.Name.Trim()}:{port.PrivatePort}";
        }

        string? hostAddress = null;
        if (port.PublicPort is > 0)
        {
            var host = ResolveHost(port.BindIp, configuredHostAddress);
            if (host is not null)
            {
                hostAddress = $"http://{host}:{port.PublicPort}";
            }
        }

        return new AddressCandidates(containerAddress, hostAddress);
    }

    /// <summary>
    /// 具体绑定 IP 优先；通配绑定回退到配置的宿主地址。
    /// </summary>
    public static string? ResolveHost(string? bindIp, string? configuredHostAddress)
    {
        if (!IsWildcardBind(bindIp))
        {
            return NormalizeHost(bindIp);
        }

        return NormalizeHost(configuredHostAddress);
    }

    public static bool IsWildcardBind(string? bindIp)
    {
        if (string.IsNullOrWhiteSpace(bindIp)) return true;
        var normalized = bindIp.Trim();
        return WildcardBindAddresses.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var host = value.Trim();
        if (host.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(host, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            {
                return null;
            }

            host = uri.Host;
        }

        host = host.TrimEnd('/');
        if (host.Length == 0) return null;

        if (!host.StartsWith('[') && host.Count(c => c == ':') >= 2)
        {
            host = $"[{host}]";
        }

        return host;
    }
}
