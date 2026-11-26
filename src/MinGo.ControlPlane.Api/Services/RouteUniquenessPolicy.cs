using MinGo.Core.Models;
using MinGo.Core.Routing;

namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// 路由保存期的域名去重校验：同一匹配键（域名 + 路径）只允许存在一条启用路由。
/// 纯函数，不碰数据库也不碰 Cloudflare，把 local / 隧道 / 通配的排列组合穷举测一遍就够。
/// </summary>
/// <remarks>
/// 为什么必须拦在保存期：重复的匹配键下发到数据面就是两条 YARP 规则，命中哪条取决于
/// 规则表的排列，跨实例还可能不一致；下发到隧道 ingress 时 hostname 规则会合并成一条，
/// 但指向哪个集群同样说不清。拓扑页的 <c>duplicate-match</c> 只做体检提示，不阻断流量歧义，
/// 真正的闸门放在这里。
/// 通配（<c>*.example.com</c>）与具体域名不算重复——实测精确域名优先命中，两者共存是有意的分层设计。
/// </remarks>
public static class RouteUniquenessPolicy
{
    public sealed record Verdict(bool IsValid, string? Error);

    /// <param name="candidate">准备保存的路由。</param>
    /// <param name="existing">数据库里的全部路由（含候选自身，靠 <paramref name="updatingRouteId"/> 排除）。</param>
    /// <param name="updatingRouteId">更新场景下被覆盖的路由 ID；新建传 null。</param>
    public static Verdict Check(
        RouteConfig candidate,
        IEnumerable<RouteConfig>? existing,
        string? updatingRouteId = null)
    {
        // 停用路由不进底层配置，也不占用匹配键；等它被启用时再走一次本校验。
        if (!candidate.Enabled)
        {
            return new Verdict(true, null);
        }

        if (!RoutePathSyntax.IsValid(candidate.Match?.Path, out var pathError))
        {
            return new Verdict(false,
                $"匹配路径语法无效：{pathError}。非法路径会让数据面拒绝整份配置，全部路由 404");
        }

        var key = RouteMatchKey.Of(candidate.Match?.Host, candidate.Match?.Path);

        foreach (var other in (existing ?? Array.Empty<RouteConfig>()).OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (!other.Enabled || other.Id == updatingRouteId)
                continue;

            if (RouteMatchKey.Of(other.Match?.Host, other.Match?.Path) != key)
                continue;

            return new Verdict(false,
                $"匹配条件 {Describe(key)} 已被启用路由 “{Label(other)}”（接入通道：{ConnectionLabel(other)}）占用。" +
                "同一域名和路径只能有一条启用路由，请改域名、改路径，或直接编辑已有那条路由。");
        }

        return new Verdict(true, null);
    }

    /// <summary>把匹配键翻成运维看得懂的一句话。</summary>
    public static string Describe(RouteMatchKey key)
    {
        var host = key.Host == RouteMatchKey.AnyHost ? "不限域名" : $"域名 “{key.Host}”";
        var path = key.Path == RouteMatchKey.AnyPath ? "任意路径" : $"路径 “{key.Path}”";
        return $"{host} + {path}";
    }

    private static string Label(RouteConfig route) =>
        string.IsNullOrWhiteSpace(route.Name) ? route.Id : route.Name.Trim();

    private static string ConnectionLabel(RouteConfig route) =>
        ConnectionIds.IsLocal(route.ConnectionId) ? "本地数据面"
        : ConnectionIds.IsGroup(route.ConnectionId) ? "数据面分组"
        : "外部隧道";
}
