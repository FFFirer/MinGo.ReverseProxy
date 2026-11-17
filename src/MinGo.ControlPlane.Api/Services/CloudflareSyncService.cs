using MinGo.ControlPlane.Api.Configuration;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.ControlPlane.Api.Services;

// ── 编排结果 DTO ──

public sealed record ReconcileReport(
    string TunnelId,
    bool IngressUpdated,
    IReadOnlyList<string> DnsCreated,
    IReadOnlyList<string> DnsDeleted,
    IReadOnlyList<string> DnsKept,
    IReadOnlyList<string> Conflicts);

public sealed record RouteValidation(bool IsValid, string? Error);

public sealed record ConnectionSummary(string Id, string Name, string Kind, string? Status);

public sealed record TunnelIngressBinding(string RouteId, string RouteName, string Host, bool Enabled);

public sealed record TunnelIngressView(
    CloudflareTunnel Tunnel,
    IngressRule[] Rules,
    TunnelIngressBinding[] LocalBindings,
    string[] MissingHosts,
    string[] StaleHosts,
    string[] UnmanagedHosts,
    string[] DriftedHosts,
    string Origin,
    string OriginSource);

/// <summary>
/// 发布编排：本地 Route 表是 MinGo 托管发布的唯一事实来源。
/// 路由变更后重算受影响隧道的 ingress 与 DNS，与 CF 侧现状合并对账。
/// </summary>
public sealed class CloudflareSyncService
{
    private readonly IApiDbService _apiDbService;
    private readonly CloudflareClient _client;
    private readonly CloudflareConfigProvider _config;
    private readonly ILogger<CloudflareSyncService> _logger;

    public CloudflareSyncService(
        IApiDbService apiDbService,
        CloudflareClient client,
        CloudflareConfigProvider config,
        ILogger<CloudflareSyncService> logger)
    {
        _apiDbService = apiDbService;
        _client = client;
        _config = config;
        _logger = logger;
    }

    // ── 状态与清单 ──

    public async Task<CloudflareStatusInfo> GetStatusAsync(CancellationToken ct)
    {
        var options = await _config.GetAsync(ct);
        if (!options.IsConfigured)
        {
            return new CloudflareStatusInfo(false, null, null, null, null);
        }

        try
        {
            var accountId = await _client.GetEffectiveAccountIdAsync(ct);
            string? accountName = null;
            try
            {
                var accounts = await _client.ListAccountsAsync(ct);
                accountName = accounts.FirstOrDefault(a => a.Id == accountId)?.Name;
            }
            catch (CloudflareException)
            {
                // 账号名仅用于展示，失败不阻断状态
            }

            var zonesCount = (await _client.ListZonesAsync(ct)).Length;
            return new CloudflareStatusInfo(true, accountId, accountName, zonesCount, null);
        }
        catch (CloudflareException ex)
        {
            _logger.LogWarning(ex, "Cloudflare 状态检查失败");
            return new CloudflareStatusInfo(true, null, null, null, ex.Message);
        }
    }

    public async Task<ConnectionSummary[]> ListConnectionsAsync(CancellationToken ct)
    {
        var items = new List<ConnectionSummary>
        {
            new(ConnectionIds.Local, "本地数据面", "local", null),
        };

        if (!(await _config.GetAsync(ct)).IsConfigured)
        {
            return items.ToArray();
        }

        try
        {
            var tunnels = await _client.ListTunnelsAsync(ct);
            items.AddRange(tunnels.Select(t =>
                new ConnectionSummary(t.Id, t.Name, "cloudflare-tunnel", t.Status)));
        }
        catch (CloudflareException ex)
        {
            _logger.LogWarning(ex, "Cloudflare 隧道列表获取失败");
        }

        return items.ToArray();
    }

    // ── 路由校验 ──

    public async Task<RouteValidation> ValidateRouteAsync(
        RouteConfig route, string? updatingRouteId, CancellationToken ct)
    {
        if (ConnectionIds.IsLocal(route.ConnectionId))
        {
            return new RouteValidation(true, null);
        }

        if (!(await _config.GetAsync(ct)).IsConfigured)
        {
            return new RouteValidation(false, "Cloudflare 未配置（请在设置页填写 API Token），无法发布到隧道");
        }

        var host = route.Match?.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return new RouteValidation(false, "发布到隧道的路由必须配置匹配域名");
        }

        if (!IngressReconciler.IsValidHostname(host))
        {
            return new RouteValidation(false, $"域名 '{host}' 格式不合法（仅支持字母/数字/连字符，可带 *. 通配）");
        }

        try
        {
            var tunnels = await _client.ListTunnelsAsync(ct);
            if (tunnels.All(t => t.Id != route.ConnectionId))
            {
                return new RouteValidation(false, $"隧道 '{route.ConnectionId}' 不存在，请先创建或刷新隧道列表");
            }
        }
        catch (CloudflareException ex)
        {
            return new RouteValidation(false, $"无法校验隧道：{ex.Message}");
        }

        var normalized = IngressReconciler.NormalizeHost(host);
        var allRoutes = await _apiDbService.GetRoutesAsync();
        foreach (var other in allRoutes)
        {
            if (updatingRouteId is not null && other.Id == updatingRouteId)
                continue;

            var otherHost = IngressReconciler.NormalizeHost(other.Match?.Host);
            if (otherHost.Length == 0 || !string.Equals(otherHost, normalized, StringComparison.Ordinal))
                continue;

            if (!ConnectionIds.IsLocal(other.ConnectionId)
                && !string.Equals(other.ConnectionId, route.ConnectionId, StringComparison.Ordinal))
            {
                return new RouteValidation(
                    false,
                    $"域名 '{host}' 已发布到隧道 '{other.ConnectionId}'（路由 '{other.Name}'），同一域名不能同时绑定多个隧道");
            }
        }

        return new RouteValidation(true, null);
    }

    public async Task<List<RouteConfig>> GetRoutesBoundToAsync(string tunnelId)
    {
        var routes = await _apiDbService.GetRoutesAsync();
        return routes
            .Where(r => !ConnectionIds.IsLocal(r.ConnectionId)
                        && string.Equals(r.ConnectionId, tunnelId, StringComparison.Ordinal))
            .ToList();
    }

    // ── 路由变更后的发布同步（最佳努力，失败不阻断路由保存） ──

    public async Task SyncRouteChangeAsync(
        RouteConfig route, string? previousConnectionId, CancellationToken ct)
    {
        foreach (var tunnelId in new[] { previousConnectionId, route.ConnectionId }
                     .Where(id => !ConnectionIds.IsLocal(id))
                     .Distinct(StringComparer.Ordinal))
        {
            try
            {
                await ReconcileTunnelAsync(tunnelId!, ct);
            }
            catch (CloudflareException ex)
            {
                _logger.LogWarning(ex, "Cloudflare 隧道 [{TunnelId}] 发布同步失败（可稍后重对账）", tunnelId);
            }
        }
    }

    public async Task SyncRouteDeletedAsync(RouteConfig deletedRoute, CancellationToken ct)
    {
        if (ConnectionIds.IsLocal(deletedRoute.ConnectionId))
            return;

        try
        {
            await ReconcileTunnelAsync(deletedRoute.ConnectionId, ct);
        }
        catch (CloudflareException ex)
        {
            _logger.LogWarning(ex, "Cloudflare 隧道 [{TunnelId}] 下架同步失败（可稍后重对账）", deletedRoute.ConnectionId);
        }
    }

    // ── 隧道级对账 ──

    public async Task<ReconcileReport> ReconcileTunnelAsync(string tunnelId, CancellationToken ct)
    {
        var routes = await GetRoutesBoundToAsync(tunnelId);
        var managedHosts = HostsOf(routes, onlyEnabled: true);
        var ownedHosts = HostsOf(routes, onlyEnabled: false);

        var config = await _client.GetConfigurationAsync(tunnelId, ct);
        var options = await _config.GetAsync(ct);
        var origin = options.ResolveOrigin((await _client.GetTunnelAsync(tunnelId, ct)).Name);
        var merge = IngressReconciler.Merge(
            config.Config?.Ingress ?? [],
            managedHosts,
            ownedHosts,
            origin.Origin,
            options.DefaultCatchAll);

        var ingressUpdated = false;
        if (merge.Changed)
        {
            await _client.PutConfigurationAsync(tunnelId, merge.Rules, ct);
            ingressUpdated = true;
        }

        var zones = await _client.ListZonesAsync(ct);
        var target = IngressReconciler.TunnelCnameTarget(tunnelId);

        var created = new List<string>();
        var deleted = new List<string>();
        var kept = new List<string>();
        var conflicts = new List<string>();

        // 托管 hostname：确保 CNAME → 本隧道
        foreach (var host in managedHosts)
        {
            var zone = IngressReconciler.FindZone(zones, host);
            if (zone is null)
            {
                conflicts.Add($"域名 '{host}' 不属于任何 Cloudflare zone，无法创建 DNS 记录");
                continue;
            }

            var records = await _client.ListDnsRecordsAsync(zone.Id, host, ct);
            var existing = records.FirstOrDefault(r =>
                string.Equals(IngressReconciler.NormalizeHost(r.Name), host, StringComparison.Ordinal));

            if (existing is null)
            {
                await _client.CreateDnsRecordAsync(zone.Id, host, target, ct);
                created.Add(host);
            }
            else if (!string.Equals(existing.Content, target, StringComparison.Ordinal))
            {
                conflicts.Add(
                    $"域名 '{host}' 已有 CNAME 指向 '{existing.Content}'（与本隧道无关），未覆盖");
            }
        }

        // 下架 hostname：仅当 CNAME 仍指向本隧道（即 MinGo 创建的）才删除
        foreach (var host in merge.RemovedHosts)
        {
            var zone = IngressReconciler.FindZone(zones, host);
            if (zone is null)
                continue;

            var records = await _client.ListDnsRecordsAsync(zone.Id, host, ct);
            var existing = records.FirstOrDefault(r =>
                string.Equals(IngressReconciler.NormalizeHost(r.Name), host, StringComparison.Ordinal));

            if (existing is null)
                continue;

            if (string.Equals(existing.Content, target, StringComparison.Ordinal))
            {
                await _client.DeleteDnsRecordAsync(zone.Id, existing.Id, ct);
                deleted.Add(host);
            }
            else
            {
                kept.Add(host);
            }
        }

        return new ReconcileReport(tunnelId, ingressUpdated, created, deleted, kept, conflicts);
    }

    // ── ingress 对账视图 ──

    public async Task<TunnelIngressView> GetIngressViewAsync(string tunnelId, CancellationToken ct)
    {
        var tunnel = await _client.GetTunnelAsync(tunnelId, ct);
        var config = await _client.GetConfigurationAsync(tunnelId, ct);
        var ingress = config.Config?.Ingress ?? [];
        var options = await _config.GetAsync(ct);
        var origin = options.ResolveOrigin(tunnel.Name);

        var routes = await GetRoutesBoundToAsync(tunnelId);
        var bindings = routes
            .Select(r => new TunnelIngressBinding(
                r.Id, r.Name, IngressReconciler.NormalizeHost(r.Match?.Host), r.Enabled))
            .Where(b => b.Host.Length > 0)
            .ToArray();

        var managedHosts = bindings
            .Where(b => b.Enabled)
            .Select(b => b.Host)
            .ToHashSet(StringComparer.Ordinal);

        // 停用路由的 hostname 仍属 MinGo 托管范围：它解释了「CF 侧有规则但本地不生效」的来源，
        // 不能和用户手写规则混为一谈
        var ownedHosts = bindings.Select(b => b.Host).ToHashSet(StringComparer.Ordinal);

        // CF 按顺序匹配，同 hostname 只有首条生效
        var serviceByHost = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var rule in ingress)
        {
            var host = IngressReconciler.NormalizeHost(rule.Hostname);
            if (host.Length > 0) serviceByHost.TryAdd(host, rule.Service);
        }

        var ingressHosts = serviceByHost.Keys.ToHashSet(StringComparer.Ordinal);

        var missing = managedHosts.Where(h => !ingressHosts.Contains(h)).ToArray();
        var stale = ownedHosts
            .Where(h => !managedHosts.Contains(h) && ingressHosts.Contains(h))
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToArray();
        var unmanaged = ingressHosts
            .Where(h => !ownedHosts.Contains(h))
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToArray();
        var drifted = managedHosts
            .Where(h => serviceByHost.TryGetValue(h, out var service)
                        && !string.Equals(service, origin.Origin, StringComparison.Ordinal))
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToArray();

        return new TunnelIngressView(
            tunnel, ingress, bindings, missing, stale, unmanaged, drifted,
            origin.Origin, origin.Source);
    }

    /// <summary>
    /// 绑定该隧道的路由 hostname 清单。onlyEnabled 为 true 时是本轮应生效的托管清单，
    /// 为 false 时是 MinGo 声明的所有权清单（含停用路由），后者用于识别陈旧托管规则。
    /// </summary>
    private static string[] HostsOf(List<RouteConfig> routes, bool onlyEnabled) => routes
        .Where(r => (!onlyEnabled || r.Enabled) && !string.IsNullOrWhiteSpace(r.Match?.Host))
        .Select(r => IngressReconciler.NormalizeHost(r.Match!.Host))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}
