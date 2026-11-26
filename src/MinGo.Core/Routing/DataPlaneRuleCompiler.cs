using MinGo.Core.Models;

namespace MinGo.Core.Routing;

/// <summary>
/// 底层配置编译器：把控制面的路由表编译成数据面真正生效的转发规则集。
/// 唯一调用方是控制面的快照编译（ConfigReplicationService），数据面只消费编译结果。
/// </summary>
/// <remarks>
/// 这里锁住三条契约：
/// <list type="number">
/// <item><description>
/// 接入通道决定「流量从哪里进来」，也决定「分组规则装进哪些数据面」——但只按
/// <c>group:&lt;GroupId&gt;</c> 过滤：绑定外部隧道的启用路由必须同样产出一条
/// 「数据面 → 后端集群」规则（隧道 ingress 一律回源到数据面，见
/// <c>IngressReconciler.Merge</c>），隧道路由与 local 路由对所有数据面一视同仁；
/// 分组路由只装进目标分组的实例。单测
/// <c>DataPlaneRuleCompilerTests.Compile_KeepsTunnelBoundRoutes</c> 与
/// <c>Compile_GroupRoutes_OnlyForTargetGroup</c> 负责防止这条契约被改回去。
/// </description></item>
/// <item><description>
/// 同一匹配键（域名 + 路径）只留一条规则，其余抑制。重复键在 YARP 里命中顺序不确定，
/// 与其赌顺序，不如按确定的次序取一条。
/// </description></item>
/// <item><description>
/// 顺序确定：按 域名 → 路径 → 路由 ID 字典序。同库同配置在任何实例上产出同一份规则集，
/// 也让「谁赢」可预测（ID 字典序最小者）。
/// </description></item>
/// </list>
/// </remarks>
public static class DataPlaneRuleCompiler
{
    /// <param name="Rules">数据面实际生效的规则，顺序即下发顺序。</param>
    /// <param name="Suppressed">被去重压掉的重复规则，不进入底层配置。</param>
    /// <param name="Warnings">给运维看的一条一句说明，与 <paramref name="Suppressed"/> 一一对应。</param>
    public sealed record Result(
        IReadOnlyList<RouteConfig> Rules,
        IReadOnlyList<RouteConfig> Suppressed,
        IReadOnlyList<string> Warnings);

    /// <summary>
    /// 编译规则集。
    /// </summary>
    /// <param name="routes">全量路由表</param>
    /// <param name="targetGroupId">
    /// 目标数据面的分组 Id；null 或 <see cref="ConnectionIds.Local"/> 表示启动默认组（未分配实例）。
    /// 分组路由只对匹配的分组生效，local 与隧道路由不受分组影响（对所有数据面生效）。
    /// </param>
    public static Result Compile(IEnumerable<RouteConfig>? routes, string? targetGroupId = null)
    {
        var ordered = (routes ?? Array.Empty<RouteConfig>())
            .Where(static route => route.Enabled)
            .Where(route => BelongsToTargetGroup(route.ConnectionId, targetGroupId))
            .Select(route => (Route: route, Key: RouteMatchKey.Of(route.Match?.Host, route.Match?.Path)))
            .OrderBy(x => x.Key.Host, StringComparer.Ordinal)
            .ThenBy(x => x.Key.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Route.Id, StringComparer.Ordinal)
            .ToList();

        var rules = new List<RouteConfig>(ordered.Count);
        var suppressed = new List<RouteConfig>();
        var warnings = new List<string>();
        var winners = new Dictionary<RouteMatchKey, RouteConfig>();

        foreach (var (route, key) in ordered)
        {
            if (winners.TryGetValue(key, out var first))
            {
                suppressed.Add(route);
                warnings.Add(
                    $"路由 “{Label(route)}” 与 “{Label(first)}” 的匹配条件重复（{key}），" +
                    "数据面只生效一条，请在路由管理里合并或删除多余规则");
                continue;
            }

            winners[key] = route;
            rules.Add(route);
        }

        return new Result(rules, suppressed, warnings);
    }

    /// <summary>
    /// 通道归属判定：local 与隧道路由对所有数据面生效；分组路由只装进目标分组的实例。
    /// </summary>
    internal static bool BelongsToTargetGroup(string? connectionId, string? targetGroupId)
    {
        if (!ConnectionIds.IsGroup(connectionId))
            return true;

        return string.Equals(ConnectionIds.GetGroupId(connectionId), targetGroupId, StringComparison.Ordinal);
    }

    private static string Label(RouteConfig route) =>
        string.IsNullOrWhiteSpace(route.Name) ? route.Id : route.Name.Trim();
}
