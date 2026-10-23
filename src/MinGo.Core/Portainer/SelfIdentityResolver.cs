namespace MinGo.Core.Portainer;

/// <summary>
/// 识别控制面自身所在的 compose stack。
/// 解析链：配置覆盖 → 主机名（容器 Id 前 12 位）→ 镜像模式 → 未识别。
/// </summary>
public static class SelfIdentityResolver
{
    public const string SourceConfig = "config";
    public const string SourceHostname = "hostname";
    public const string SourceImage = "image";
    public const string SourceNone = "none";

    public static SelfIdentity Resolve(
        string? hostname,
        IEnumerable<ContainerSnapshot> containers,
        IEnumerable<string>? configuredStacks,
        IEnumerable<string>? imagePatterns)
    {
        var all = containers as IReadOnlyCollection<ContainerSnapshot> ?? containers.ToList();

        var configured = Normalize(configuredStacks);
        if (configured.Count > 0)
        {
            return Build(SourceConfig, configured, all);
        }

        if (!string.IsNullOrWhiteSpace(hostname))
        {
            var host = hostname.Trim();
            var matches = all.Where(c => HostnameMatches(host, c.Id)).ToList();
            var stacks = Normalize(matches.Select(c => c.Stack));
            if (stacks.Count > 0)
            {
                return Build(SourceHostname, stacks, all);
            }
        }

        var patterns = Normalize(imagePatterns);
        if (patterns.Count > 0)
        {
            var matches = all
                .Where(c => c.IsRunning && patterns.Any(p => c.Image.Contains(p, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var stacks = Normalize(matches.Select(c => c.Stack));
            if (stacks.Count > 0)
            {
                return Build(SourceImage, stacks, all);
            }
        }

        return SelfIdentity.None;
    }

    /// <summary>
    /// 容器内主机名默认等于自身容器 Id 的前 12 位，因此用前缀匹配。
    /// 自定义 hostname 或 host 网络模式下不会命中，交由镜像兜底。
    /// </summary>
    private static bool HostnameMatches(string hostname, string containerId) =>
        hostname.Length > 0 &&
        containerId.Length > 0 &&
        hostname.Length <= containerId.Length &&
        containerId.StartsWith(hostname, StringComparison.OrdinalIgnoreCase);

    private static SelfIdentity Build(
        string source,
        IReadOnlyList<string> stacks,
        IReadOnlyCollection<ContainerSnapshot> all)
    {
        var count = all.Count(c =>
            c.Stack is not null &&
            stacks.Any(s => string.Equals(s, c.Stack, StringComparison.OrdinalIgnoreCase)));

        return new SelfIdentity(true, source, stacks, count);
    }

    private static List<string> Normalize(IEnumerable<string?>? values)
    {
        if (values is null) return [];

        var result = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var trimmed = value.Trim();
            if (!result.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
