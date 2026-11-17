namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// Cloudflare 隧道 ingress 规则合并器（纯函数，可单测）。
/// </summary>
/// <remarks>
/// 所有权识别：hostname 出现在托管清单（绑定该隧道的本地启用路由）即由 MinGo 托管；
/// 其余规则原样保留。托管规则回源地址统一为网关 origin。
/// </remarks>
public static class IngressReconciler
{
    public sealed record MergeResult(
        IngressRule[] Rules,
        string[] AddedHosts,
        string[] RemovedHosts,
        bool Changed);

    public static string NormalizeHost(string? host) =>
        (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

    /// <summary>
    /// 域名语法校验：允许 <c>*.example.com</c> 通配，拒绝 scheme/路径/端口/空白。
    /// </summary>
    public static bool IsValidHostname(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var h = NormalizeHost(host);
        if (h.Length > 253 || h.Contains('/') || h.Contains(':'))
            return false;

        var body = h.StartsWith("*.", StringComparison.Ordinal) ? h[2..] : h;
        if (body.Length == 0)
            return false;

        foreach (var label in body.Split('.'))
        {
            if (label.Length == 0 || label.Length > 63)
                return false;
            if (label[0] == '-' || label[^1] == '-')
                return false;
            if (!label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 合并托管清单与现有规则：
    /// 托管 hostname 覆盖更新（缺失则插入兜底前）；service 指向网关 origin 且已不在托管清单的规则视为陈旧托管规则，移除；
    /// 其余规则原样保留；末尾无 hostname 的兜底规则保留用户现有内容，缺失时追加。
    /// </summary>
    public static MergeResult Merge(
        IReadOnlyList<IngressRule> current,
        IReadOnlyCollection<string> managedHostnames,
        string origin,
        string defaultCatchAll)
    {
        current ??= [];
        managedHostnames ??= [];

        var managed = managedHostnames
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(NormalizeHost)
            .ToHashSet(StringComparer.Ordinal);

        var result = new List<IngressRule>(current.Count + managed.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var removed = new List<string>();
        var changed = false;

        foreach (var rule in current)
        {
            var host = NormalizeHost(rule.Hostname);

            // 兜底规则（无 hostname）原样保留
            if (host.Length == 0)
            {
                result.Add(rule);
                continue;
            }

            if (managed.Contains(host))
            {
                var isSame = string.Equals(rule.Service, origin, StringComparison.Ordinal)
                             && string.IsNullOrWhiteSpace(rule.Path);
                if (isSame)
                {
                    result.Add(rule);
                }
                else
                {
                    result.Add(new IngressRule(rule.Hostname, origin, null, null));
                    changed = true;
                }
                placed.Add(host);
                continue;
            }

            // 指向网关 origin 但已不在托管清单 → 陈旧托管规则
            if (string.Equals(rule.Service, origin, StringComparison.Ordinal))
            {
                removed.Add(host);
                changed = true;
                continue;
            }

            result.Add(rule);
        }

        // 托管清单中缺失的 hostname 插入在首个兜底规则之前，保证可被匹配；
        // CF 正常数据兜底必在末尾，此时与追加等价
        var insertIndex = result.FindIndex(r => NormalizeHost(r.Hostname).Length == 0);
        if (insertIndex < 0)
            insertIndex = result.Count;

        var added = new List<string>();
        foreach (var host in managed)
        {
            if (placed.Contains(host))
                continue;
            result.Insert(insertIndex, new IngressRule(host, origin, null, null));
            insertIndex++;
            added.Add(host);
            changed = true;
        }

        // 确保末尾存在兜底规则；已有（含用户自定义内容）则不动
        if (result.Count == 0 || NormalizeHost(result[^1].Hostname).Length != 0)
        {
            result.Add(new IngressRule(string.Empty, defaultCatchAll, null, null));
            changed = true;
        }

        return new MergeResult(
            result.ToArray(),
            added.ToArray(),
            removed.ToArray(),
            changed);
    }

    /// <summary>
    /// 从 zones 中找 hostname 的最长后缀匹配 zone；通配符前缀 <c>*.</c> 不参与匹配。
    /// </summary>
    public static CloudflareZone? FindZone(IEnumerable<CloudflareZone> zones, string hostname)
    {
        var host = NormalizeHost(hostname);
        if (host.StartsWith("*.", StringComparison.Ordinal))
            host = host[2..];

        return zones
            .Select(z => (Zone: z, Suffix: NormalizeHost(z.Name)))
            .Where(x => host.Equals(x.Suffix, StringComparison.Ordinal)
                        || host.EndsWith("." + x.Suffix, StringComparison.Ordinal))
            .OrderByDescending(x => x.Suffix.Length)
            .Select(x => x.Zone)
            .FirstOrDefault();
    }

    public static string TunnelCnameTarget(string tunnelId) => $"{tunnelId}.cfargotunnel.com";
}
