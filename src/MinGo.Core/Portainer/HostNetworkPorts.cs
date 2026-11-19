namespace MinGo.Core.Portainer;

/// <summary>
/// 宿主网络（network_mode: host）相关纯函数。
/// host 模式容器共享宿主网络栈：Docker 列表接口中其 Networks 与 Ports 均为空，
/// 需要合成网络归属，并从 inspect 的 Config.ExposedPorts 合成端口。
/// </summary>
public static class HostNetworkPorts
{
    public const string HostNetworkName = "host";

    public static bool IsHostMode(string? networkMode) =>
        string.Equals(networkMode?.Trim(), "host", StringComparison.OrdinalIgnoreCase);

    public static bool IsHostNetwork(string? networkName) =>
        string.Equals(networkName?.Trim(), HostNetworkName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ExposedPorts（如 "80/tcp"）→ 端口绑定。host 模式无 NAT，宿主端口与容器端口相同，
    /// 且绑定到全部宿主地址（通配）。
    /// </summary>
    public static IReadOnlyList<PortBinding> FromExposedPorts(IEnumerable<string>? exposedPorts)
    {
        var result = new List<PortBinding>();

        if (exposedPorts is null)
        {
            return result;
        }

        foreach (var entry in exposedPorts)
        {
            var parsed = ParseExposedPort(entry);
            if (parsed is not null && string.Equals(parsed.Value.Protocol, "tcp", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new PortBinding(parsed.Value.Port, parsed.Value.Port, null, "tcp"));
            }
        }

        return result
            .OrderBy(p => p.PrivatePort)
            .ToList();
    }

    private static (int Port, string Protocol)? ParseExposedPort(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return null;
        }

        var parts = entry.Trim().Split('/');
        if (parts.Length is < 1 or > 2 ||
            !int.TryParse(parts[0], out var port) ||
            port <= 0)
        {
            return null;
        }

        return (port, parts.Length == 2 ? parts[1] : string.Empty);
    }
}
