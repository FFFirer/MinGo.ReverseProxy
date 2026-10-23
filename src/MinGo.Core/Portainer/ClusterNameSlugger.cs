using System.Text;

namespace MinGo.Core.Portainer;

/// <summary>
/// 将 compose project 名称转成可用的 cluster 名称：
/// 小写、非法字符转 '-'、折叠连续 '-'、去首尾 '-'；为空时使用回退值。
/// </summary>
public static class ClusterNameSlugger
{
    private const string UltimateFallback = "cluster";

    public static string Slug(string? value, string? fallback = null)
    {
        var slug = Transform(value);
        if (slug.Length > 0) return slug;

        var fallbackSlug = Transform(fallback);
        return fallbackSlug.Length > 0 ? fallbackSlug : UltimateFallback;
    }

    private static string Transform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            var isAllowed = ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-';
            var next = isAllowed ? ch : '-';

            if (next == '-' && builder.Length > 0 && builder[^1] == '-')
            {
                continue;
            }

            builder.Append(next);
        }

        return builder.ToString().Trim('-');
    }
}
