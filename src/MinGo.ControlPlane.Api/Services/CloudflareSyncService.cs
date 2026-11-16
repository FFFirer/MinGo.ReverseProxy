using Microsoft.Extensions.Options;
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
    string[] UnmanagedHosts);

/// <summary>
/// 发布编排：本地 Route 表是 MinGo 托管发布的唯一事实来源。
/// 路由变更后重算受影响隧道的 ingress 与 DNS，与 CF 侧现状合并对账。
/// </summary>
public sealed class CloudflareSyncService
{
    private readonly IApiDbService _apiDbService;
    private readonly CloudflareClient _client;
    private readonly CloudflareOptions _options;
    private readonly ILogger<CloudflareSyncService> _logger;

    public CloudflareSyncService(
        IApiDbService apiDbService,
        CloudflareClient client,
        IOptions<CloudflareOptions> options,
        ILogger<CloudflareSyncService> logger)
    {
        _apiDbService = apiDbService;
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    // ── 状态与清单 ──

    public async Task<CloudflareStatusInfo> GetStatusAsync(CancellationToken ct)
    {
        if (!_options.IsConfigured)
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

        if (!_options.IsConfigured)
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

        if (!_options.IsConfigured)
        {
            return new RouteValidation(false, "Cloudflare 未配置（Cloudflare:ApiToken 为空），无法发布到隧道");
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
        var managedHosts = routes
            .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Match?.Host))
            .Select(r => IngressReconciler.NormalizeHost(r.Match!.Host))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var config = await _client.GetConfigurationAsync(tunnelId, ct);
        var merge = IngressReconciler.Merge(
            config.Ingress,
            managedHosts,
            _options.DataPlaneOrigin,
            _options.DefaultCatchAll);

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

        var ingressHosts = config.Ingress
            .Where(r => IngressReconciler.NormalizeHost(r.Hostname).Length > 0)
            .Select(r => IngressReconciler.NormalizeHost(r.Hostname))
            .ToHashSet(StringComparer.Ordinal);

        var missing = managedHosts.Where(h => !ingressHosts.Contains(h)).ToArray();
        var unmanaged = ingressHosts
            .Where(h => !managedHosts.Contains(h))
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToArray();

        return new TunnelIngressView(tunnel, config.Ingress, bindings, missing, unmanaged);
    }
}
