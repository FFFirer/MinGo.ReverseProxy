using System.Net.Http.Json;
using System.Text.Json;

namespace MinGo.ControlPlane.Api.Services;

// ── Cloudflare API DTO ──

public sealed record CloudflareAccount(string Id, string Name);

public sealed record CloudflareZone(string Id, string Name, string Status);

public sealed record CloudflareTunnel(
    string Id,
    string Name,
    string? Status,
    string? ConfigSrc,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ConnsActiveAt,
    DateTimeOffset? ConnsInactiveAt);

public sealed record TunnelConnection(
    string Id,
    string? ColoName,
    DateTimeOffset? OpenedAt,
    string? OriginIp,
    string? ClientVersion);

public sealed record IngressRule(
    string Hostname,
    string Service,
    string? Path,
    OriginRequestConfig? OriginRequest);

public sealed record OriginRequestConfig(
    bool? NoTlsVerify,
    string? HttpHostHeader,
    int? ConnectTimeout,
    int? TlsTimeout,
    int? KeepAliveTimeout,
    int? KeepAliveConnections,
    string? OriginServerName);

public sealed record TunnelConfiguration(IngressRule[] Ingress, string? Source, int Version);

public sealed record DnsRecord(string Id, string Type, string Name, string Content, bool Proxied);

public sealed record CloudflareStatusInfo(
    bool Configured,
    string? AccountId,
    string? AccountName,
    int? ZonesCount,
    string? Error);

// ── 客户端 ──

/// <summary>
/// Cloudflare REST API 封装（/client/v4）。凭据经 <see cref="CloudflareConfigProvider"/>
/// 解析：设置页保存的值优先，未保存时回退到配置节。
/// </summary>
public sealed class CloudflareClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CloudflareConfigProvider _config;
    private readonly ILogger<CloudflareClient> _logger;

    public CloudflareClient(
        IHttpClientFactory httpClientFactory,
        CloudflareConfigProvider config,
        ILogger<CloudflareClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var options = await _config.GetAsync(ct);
        if (!options.IsConfigured)
        {
            throw new CloudflareException("Cloudflare 未配置（请在设置页填写 API Token）");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));

        var client = _httpClientFactory.CreateClient("Cloudflare");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", options.ApiToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, timeoutCts.Token);
        var payload = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        // 令牌只在服务端日志脱敏，绝不回显
        if (!response.IsSuccessStatusCode)
        {
            var message = TryReadErrors(payload, out var errors)
                ? $"Cloudflare API 请求失败 ({(int)response.StatusCode}): {string.Join("; ", errors)}"
                : $"Cloudflare API 请求失败 ({(int)response.StatusCode})";
            throw new CloudflareException(message, (int)response.StatusCode, errors);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default!;
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
        {
            var errors = root.TryGetProperty("errors", out var errorsNode)
                ? errorsNode.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() ?? "未知错误" : "未知错误")
                    .ToArray()
                : Array.Empty<string>();
            throw new CloudflareException(
                $"Cloudflare API 拒绝请求: {string.Join("; ", errors)}", null, errors);
        }

        var result = root.TryGetProperty("result", out var resultNode)
            ? resultNode.Deserialize<T>(JsonOpts)
            : default;
        return result!;
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static bool TryReadErrors(string payload, out string[] errors)
    {
        errors = Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("errors", out var node))
            {
                errors = node.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() ?? "未知错误" : "未知错误")
                    .ToArray();
                return errors.Length > 0;
            }
        }
        catch (JsonException)
        {
        }
        return false;
    }

    private async Task<string> ResolveAccountIdAsync(CancellationToken ct)
    {
        var options = await _config.GetAsync(ct);
        if (!string.IsNullOrWhiteSpace(options.AccountId))
            return options.AccountId;
        throw new CloudflareException("Cloudflare AccountId 未配置，无法确定账号");
    }

    // ── 账号 / Zones ──

    public Task<CloudflareAccount[]> ListAccountsAsync(CancellationToken ct = default) =>
        SendAsync<CloudflareAccount[]>(HttpMethod.Get, "accounts", null, ct);

    /// <summary>解析账号 ID：配置优先，其次自动发现（唯一账号时）。</summary>
    public async Task<string> GetEffectiveAccountIdAsync(CancellationToken ct = default)
    {
        var options = await _config.GetAsync(ct);
        if (!string.IsNullOrWhiteSpace(options.AccountId))
            return options.AccountId;

        var accounts = await ListAccountsAsync(ct);
        if (accounts.Length == 1)
            return accounts[0].Id;
        if (accounts.Length == 0)
            throw new CloudflareException("Cloudflare 账号下没有可用账户");

        throw new CloudflareException(
            $"存在多个 Cloudflare 账号（{string.Join(", ", accounts.Select(a => a.Name))}），请在设置页配置 AccountId");
    }

    public Task<CloudflareZone[]> ListZonesAsync(CancellationToken ct = default) =>
        SendAsync<CloudflareZone[]>(HttpMethod.Get, "zones?per_page=50", null, ct);

    // ── 隧道 CRUD ──

    public async Task<CloudflareTunnel[]> ListTunnelsAsync(CancellationToken ct = default) =>
        await SendAsync<CloudflareTunnel[]>(
            HttpMethod.Get, $"accounts/{await ResolveAccountIdAsync(ct)}/cfd_tunnel", null, ct);

    public async Task<CloudflareTunnel> GetTunnelAsync(string tunnelId, CancellationToken ct = default) =>
        await SendAsync<CloudflareTunnel>(
            HttpMethod.Get, $"accounts/{await ResolveAccountIdAsync(ct)}/cfd_tunnel/{tunnelId}", null, ct);

    public async Task<CloudflareTunnel> CreateTunnelAsync(string name, CancellationToken ct = default) =>
        await SendAsync<CloudflareTunnel>(
            HttpMethod.Post, $"accounts/{await ResolveAccountIdAsync(ct)}/cfd_tunnel",
            new { name, config_src = "cloudflare" }, ct);

    public async Task<CloudflareTunnel> UpdateTunnelAsync(string tunnelId, string name, CancellationToken ct = default) =>
        await SendAsync<CloudflareTunnel>(
            HttpMethod.Patch, $"accounts/{await ResolveAccountIdAsync(ct)}/cfd_tunnel/{tunnelId}",
            new { name }, ct);

    public async Task<CloudflareTunnel> DeleteTunnelAsync(string tunnelId, CancellationToken ct = default) =>
        await SendAsync<CloudflareTunnel>(
            HttpMethod.Delete, $"accounts/{await ResolveAccountIdAsync(ct)}/cfd_tunnel/{tunnelId}", null, ct);

    // ── 连接与 ingress 配置 ──

    public async Task<TunnelConnection[]> ListConnectionsAsync(string tunnelId, CancellationToken ct = default) =>
        await SendAsync<TunnelConnection[]>(
            HttpMethod.Get, $"accounts/{await ResolveAccountIdAsync(ct)}/cfd_tunnel/{tunnelId}/connections", null, ct);

    public async Task<TunnelConfiguration> GetConfigurationAsync(string tunnelId, CancellationToken ct = default) =>
        await SendAsync<TunnelConfiguration>(
            HttpMethod.Get, $"accounts/{await ResolveAccountIdAsync(ct)}/cfd_tunnel/{tunnelId}/configurations", null, ct);

    public async Task<TunnelConfiguration> PutConfigurationAsync(
        string tunnelId, IngressRule[] ingress, CancellationToken ct = default) =>
        await SendAsync<TunnelConfiguration>(
            HttpMethod.Put, $"accounts/{await ResolveAccountIdAsync(ct)}/cfd_tunnel/{tunnelId}/configurations",
            new { config = new { ingress } }, ct);

    // ── DNS 记录 ──

    public Task<DnsRecord[]> ListDnsRecordsAsync(string zoneId, string? name = null, CancellationToken ct = default)
    {
        var query = $"zones/{zoneId}/dns_records?per_page=100&type=CNAME";
        if (!string.IsNullOrWhiteSpace(name))
            query += $"&name={Uri.EscapeDataString(name)}";
        return SendAsync<DnsRecord[]>(HttpMethod.Get, query, null, ct);
    }

    public Task<DnsRecord> CreateDnsRecordAsync(
        string zoneId, string name, string content, CancellationToken ct = default) =>
        SendAsync<DnsRecord>(
            HttpMethod.Post, $"zones/{zoneId}/dns_records",
            new { type = "CNAME", name, content, proxied = true }, ct);

    public Task<DnsRecord> DeleteDnsRecordAsync(string zoneId, string recordId, CancellationToken ct = default) =>
        SendAsync<DnsRecord>(
            HttpMethod.Delete, $"zones/{zoneId}/dns_records/{recordId}", null, ct);
}