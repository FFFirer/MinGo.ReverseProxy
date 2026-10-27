using System.Net;
using MinGo.DataPlane.Kestrel;

namespace MinGo.DataPlane.Middleware;

/// <summary>
/// 明文请求是否应跳转到 HTTPS。准入 = 证书命中（精确或 <c>*.example.com</c> 通配），不回退默认证书。
/// </summary>
public static class HttpsRedirectPolicy
{
    private const string HealthPathPrefix = "/healthz";

    /// <summary>
    /// 按短路顺序判定：开关 → 已加密 → 健康检查 → Host 可用性 → 配置例外 → 证书命中。
    /// </summary>
    public static bool NeedsRedirect(
        HttpContext context,
        HttpsRedirectOptions options,
        DataPlaneCertificateSelector certificateSelector)
    {
        if (!options.Enabled)
            return false;

        var request = context.Request;

        if (request.IsHttps)
            return false;

        if (request.Path.StartsWithSegments(HealthPathPrefix))
            return false;

        var host = request.Host.Host;
        if (string.IsNullOrWhiteSpace(host))
            return false;

        // 内网直连 / 探针 / curl 用 IP，跳过去只会撞上 SNI 不匹配
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return false;
        if (IPAddress.TryParse(host, out _))
            return false;

        if (MatchesAnyHost(host, options.HttpOnlyHosts))
            return false;

        return certificateSelector.TryGetCertificate(host);
    }

    /// <summary>精确匹配或 <c>*.suffix</c> 后缀匹配，大小写不敏感；<c>*.example.com</c> 不匹配裸域名。</summary>
    internal static bool MatchesAnyHost(string host, IEnumerable<string> patterns)
    {
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();

        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var pattern = raw.Trim().TrimEnd('.').ToLowerInvariant();
            if (pattern.StartsWith("*."))
            {
                if (normalized.EndsWith(pattern[1..], StringComparison.Ordinal))
                    return true;
            }
            else if (string.Equals(normalized, pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
