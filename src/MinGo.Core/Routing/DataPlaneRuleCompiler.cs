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
/// 接入通道决定「流量从哪里进来」，也决定「节点规则装进哪些数据面」——只按
/// <c>node:&lt;NodeId&gt;</c> 过滤：绑定外部隧道的启用路由必须同样产出一条
/// 「数据面 → 后端集群」规则（隧道 ingress 一律回源到数据面，见
/// <c>IngressReconciler.Merge</c>），隧道路由对所有已绑定节点的数据面一视同仁；
/// 节点路由只装进目标节点的实例；未绑定节点的实例收到空规则集（不推送配置）。
/// 单测
/// <c>DataPlaneRuleCompilerTests.Compile_KeepsTunnelBoundRoutes</c> 与
/// <c>Compile_NodeRoutes_OnlyForTargetNode</c> 负责防止这条契约被改回去。
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
    /// <param name="targetNodeId">
    /// 目标数据面绑定的节点 Id；null 表示未绑定实例（不推送配置，返回空规则集）。
    /// 节点路由只对匹配的节点生效，隧道路由对所有已绑定节点的数据面生效。
    /// </param>
    public static Result Compile(IEnumerable<RouteConfig>? routes, string? targetNodeId)
    {
        var ordered = (routes ?? Array.Empty<RouteConfig>())
            .Where(static route => route.Enabled)
            .Where(route => BelongsToTargetNode(route.ConnectionId, targetNodeId))
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
    /// 通道归属判定：未绑定实例（null）不推送任何规则；隧道路由对所有已绑定实例生效；
    /// 节点路由只装进目标节点的实例。
    /// </summary>
    internal static bool BelongsToTargetNode(string? connectionId, string? targetNodeId)
    {
        if (targetNodeId == null)
            return false;

        if (!ConnectionIds.IsNode(connectionId))
            return true;

        return string.Equals(ConnectionIds.GetNodeId(connectionId), targetNodeId, StringComparison.Ordinal);
    }

    private static string Label(RouteConfig route) =>
        string.IsNullOrWhiteSpace(route.Name) ? route.Id : route.Name.Trim();
}
