using MinGo.Core.Models;

namespace MinGo.Core.Routing;

/// <summary>
/// 底层配置编译器：把控制面的路由表编译成数据面真正生效的转发规则集。
/// 控制面下发快照（gRPC）和进程内直连数据库两种加载方式都走这里，保证两条路产出同一份规则。
/// </summary>
/// <remarks>
/// 这里锁住三条契约：
/// <list type="number">
/// <item><description>
/// 接入通道只决定「流量从哪里进来」，不决定「规则装在哪个进程」。绑定外部隧道的启用路由
/// 必须同样产出一条「数据面 → 后端集群」规则：隧道 ingress 一律回源到数据面
/// （见 <c>IngressReconciler.Merge</c>），缺了这条内部规则，隧道流量到数据面就是 404。
/// 编译过程完全不读 <see cref="RouteConfig.ConnectionId"/>，正是这条契约的实现方式，
/// 单测 <c>DataPlaneRuleCompilerTests.Compile_KeepsTunnelBoundRoutes</c> 负责防止它被改回去。
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

    public static Result Compile(IEnumerable<RouteConfig>? routes)
    {
        var ordered = (routes ?? Array.Empty<RouteConfig>())
            .Where(static route => route.Enabled)
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

    private static string Label(RouteConfig route) =>
        string.IsNullOrWhiteSpace(route.Name) ? route.Id : route.Name.Trim();
}
