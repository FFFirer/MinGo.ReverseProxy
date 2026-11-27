using System.Net;

namespace MinGo.Core.Models;

/// <summary>
/// 路由域名收集的纯策略（无 IO，便于单测）：从路由集合派生需要证书自动登记的数据面域名。
/// 口径：仅启用路由 + 数据面节点通道（空 ConnectionId 视同本地数据面）；Cloudflare 隧道由边缘终结 TLS，自有证书管理，不参与。
/// </summary>
public static class RouteCertEnrollmentPolicy
{
    /// <summary>
    /// 收集需要证书的域名（裸域归一化 → 是否泛域）。
    /// AnyHost（null/空/*）、IP 字面量、localhost、无点域名、端口残留等一律跳过。
    /// </summary>
    public static IReadOnlyDictionary<string, bool> CollectHosts(IEnumerable<RouteConfig> routes)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var route in routes)
        {
            if (!route.Enabled)
            {
                continue;
            }

            // 仅数据面通道：node:* 或空（历史数据按本地数据面默认通道处理）
            if (!ConnectionIds.IsNode(route.ConnectionId) && !string.IsNullOrEmpty(route.ConnectionId))
            {
                continue;
            }

            var host = NormalizeHost(route.Match?.Host);
            if (host.Length == 0 || host == "*")
            {
                continue;
            }

            var isWildcard = host.StartsWith("*.", StringComparison.Ordinal);
            var domain = isWildcard ? host[2..] : host;

            if (domain.Length == 0
                || domain == "localhost"
                || !domain.Contains('.')
                || IPAddress.TryParse(domain, out _)
                || !CertificateEnrollmentPolicy.IsValidDomain(domain))
            {
                continue;
            }

            result[domain] = result.TryGetValue(domain, out var wildcard) ? wildcard || isWildcard : isWildcard;
        }

        return result;
    }

    /// <summary>host 规整：去空白、转小写、剥端口、去尾点（与数据面 SNI 归一化同口径）。</summary>
    private static string NormalizeHost(string? host)
    {
        var value = (host ?? string.Empty).Trim().ToLowerInvariant();
        var colon = value.IndexOf(':');
        if (colon >= 0)
        {
            value = value[..colon];
        }

        return value.Trim().TrimEnd('.');
    }
}