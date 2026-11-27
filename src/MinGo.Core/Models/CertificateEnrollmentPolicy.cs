namespace MinGo.Core.Models;

/// <summary>
/// 证书接入的纯策略决策（无 IO，便于单测）：
/// 域名归一化、泛域名导入行规划、(DomainName, Thumbprint) 幂等判定。
/// </summary>
public static class CertificateEnrollmentPolicy
{
    /// <summary>域名归一化：去空白、转小写。</summary>
    public static string NormalizeDomain(string domain) => domain.Trim().ToLowerInvariant();

    /// <summary>
    /// 导入行域名规划：非泛域名只导入裸域一行；泛域名证书（SAN 含裸域 + *.domain）
    /// 导入两行，与 <see cref="CertificateSnapshotPolicy"/> 的通配覆盖语义对齐。
    /// </summary>
    public static IReadOnlyList<string> GetImportDomains(string domain, bool isWildcard)
    {
        var normalized = NormalizeDomain(domain);
        return isWildcard ? [normalized, $"*.{normalized}"] : [normalized];
    }

    /// <summary>
    /// 幂等判定：对每个目标域名，已存在「同域名 + 同指纹」的证书行则跳过；
    /// 返回需要新增导入的域名集合。指纹比较不区分大小写。
    /// </summary>
    public static IReadOnlyList<string> FilterNewDomains(
        IEnumerable<CertificateConfig> existingCertificates,
        IReadOnlyList<string> importDomains,
        string thumbprint)
    {
        var existing = existingCertificates
            .Where(c => !string.IsNullOrWhiteSpace(c.Thumbprint))
            .Select(c => (Domain: NormalizeDomain(c.DomainName), Thumbprint: c.Thumbprint.ToLowerInvariant()))
            .ToHashSet();

        return importDomains
            .Where(d => !existing.Contains((NormalizeDomain(d), thumbprint.ToLowerInvariant())))
            .ToList();
    }

    /// <summary>登记域名合法性：非空、无协议头、无端口、无通配符（泛域名用开关表示）。</summary>
    public static bool IsValidDomain(string domain)
    {
        var normalized = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("://")
            || normalized.Contains(':')
            || normalized.Contains('*')
            || normalized.Contains('/'))
        {
            return false;
        }

        // 至少一个点、每段非空且只含字母数字连字符
        var labels = normalized.Split('.');
        return labels.Length >= 2
               && labels.All(l => l.Length > 0
                                  && l.All(c => char.IsLetterOrDigit(c) || c == '-'));
    }
}
