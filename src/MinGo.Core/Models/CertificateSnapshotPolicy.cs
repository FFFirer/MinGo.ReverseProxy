namespace MinGo.Core.Models;

/// <summary>
/// 证书下发策略：决定哪些证书记录进入配置快照，以及同一域名保留哪一张。
/// <para>
/// 只回答两件事：证书是否可加载（有字节、有域名），以及同域名下用哪一张。
/// 刻意<b>不</b>判断是否在时效内——过期证书必须照常下发，
/// 由数据面在 TLS 握手中呈现并在日志里告警；这里是
/// <c>enable-https-redirect</c> fail-closed 的前提。
/// </para>
/// </summary>
public static class CertificateSnapshotPolicy
{
    /// <summary>
    /// 从存储记录中挑出可下发的证书，每个域名一张，按域名（小写、序数）排序。
    /// </summary>
    /// <param name="certificates">数据库中的全部证书记录（顺序无意义）</param>
    /// <param name="now">判定"已过期"的统一时刻，便于测试注入</param>
    public static IReadOnlyList<CertificateConfig> SelectForDelivery(
        IEnumerable<CertificateConfig> certificates, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(certificates);

        return certificates
            .Where(c => c.CertificateData is { Length: > 0 })
            .Where(c => !string.IsNullOrWhiteSpace(c.DomainName))
            .Select(c => (Key: NormalizeDomain(c.DomainName), Cert: c))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => PickForDomain(g.Select(x => x.Cert), now))
            .ToList();
    }

    /// <summary>
    /// 同一域名内择优：优先未过期的一张，都过期则取过期最晚的一张；
    /// <see cref="CertificateConfig.ExpiresAt"/> 为 null 视为长期有效（最晚）。
    /// 时间相同按 Id 序数排序，保证重复下发顺序稳定。
    /// </summary>
    private static CertificateConfig PickForDomain(IEnumerable<CertificateConfig> group, DateTimeOffset now)
    {
        var candidates = group.ToList();
        var unexpired = candidates.Where(c => !IsExpired(c, now)).ToList();
        var pool = unexpired.Count > 0 ? unexpired : candidates;

        return pool
            .OrderByDescending(c => c.ExpiresAt ?? DateTimeOffset.MaxValue)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .First();
    }

    private static bool IsExpired(CertificateConfig cert, DateTimeOffset now)
        => cert.ExpiresAt is { } expiresAt && expiresAt <= now;

    private static string NormalizeDomain(string domain) => domain.Trim().ToLowerInvariant();
}
