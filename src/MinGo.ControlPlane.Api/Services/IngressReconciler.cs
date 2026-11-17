namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// Cloudflare 隧道 ingress 规则合并器（纯函数，可单测）。
/// </summary>
/// <remarks>
/// 所有权识别以本地路由表为唯一权威：绑定该隧道的路由（含停用）的 hostname 集合即 MinGo 声明的
/// 所有权范围，其中启用路由的 hostname 是本轮生效清单。规则归属不再依赖 service 字符串是否等于
/// origin —— 否则回源地址一旦按隧道变化，上一轮写入的规则会被误判成用户手写规则而永久残留。
/// service 等于当前 origin 仅作为历史痕迹兜底，用于清理早期版本写入的无主规则。
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
    /// 生效清单（managed）内的 hostname 覆盖更新为 origin 并保留原位置，同 hostname 的重复条目合并为一条；
    /// 属于所有权清单（owned，含停用路由）但已不生效的 hostname 视为解绑，移除；
    /// 其余 hostname 指向 origin 的历史痕迹规则移除，其它用户规则原样保留；
    /// 末尾无 hostname 的兜底规则保留用户现有内容，缺失时追加。
    /// </summary>
    /// <param name="current">CF 侧现有 ingress 规则。</param>
    /// <param name="managedHostnames">本轮应生效的 hostname（绑定该隧道的启用路由）。</param>
    /// <param name="ownedHostnames">MinGo 声明所有权的 hostname（绑定该隧道的全部路由，含停用）。</param>
    /// <param name="origin">该隧道的生效回源地址，由连接器所在网络范围决定。</param>
    public static MergeResult Merge(
        IReadOnlyList<IngressRule> current,
        IReadOnlyCollection<string> managedHostnames,
        IReadOnlyCollection<string> ownedHostnames,
        string origin,
        string defaultCatchAll)
    {
        current ??= [];

        var managed = ToHostSet(managedHostnames);
        var owned = ToHostSet(ownedHostnames);
        owned.UnionWith(managed);

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
                // 同 hostname 只认第一条，重复条目（历史 origin 变更或用户手写）合并掉
                if (!placed.Add(host))
                {
                    changed = true;
                    continue;
                }

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
                continue;
            }

            // 属于所有权清单但已退出生效清单 → 路由停用或解绑，托管规则摘除
            if (owned.Contains(host)
                || string.Equals(rule.Service, origin, StringComparison.Ordinal))
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

    private static HashSet<string> ToHostSet(IReadOnlyCollection<string>? hostnames) =>
        (hostnames ?? [])
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(NormalizeHost)
            .ToHashSet(StringComparer.Ordinal);

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
