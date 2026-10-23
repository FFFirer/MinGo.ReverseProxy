using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MinGo.ControlPlane.Api.Configuration;
using MinGo.ControlPlane.Api.Models.Portainer;
using MinGo.Core.Portainer;

namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// 容器优先的 Portainer 发现服务：一次枚举 endpoint → 容器 → 网络，
/// 按 compose project 分组，并应用自身识别与排除策略。
/// </summary>
public sealed class PortainerDiscoveryService
{
    private const string StandaloneGroupName = "(未分组)";

    private readonly PortainerClient _client;
    private readonly IMemoryCache _cache;
    private readonly PortainerOptions _options;
    private readonly ILogger<PortainerDiscoveryService> _logger;

    public PortainerDiscoveryService(
        PortainerClient client,
        IMemoryCache cache,
        IOptions<PortainerOptions> options,
        ILogger<PortainerDiscoveryService> logger)
    {
        _client = client;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PortainerStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        if (!_client.IsConfigured)
        {
            return new PortainerStatusResponse(
                _options.Enabled,
                BaseUrlOf(_options),
                false,
                null,
                null,
                _options.ApiKeyConfigured,
                PortainerSelfIdentity.Empty,
                _client.NotConfiguredReason);
        }

        try
        {
            // status 本身不读缓存，确保“测试连接”反映当前状态；成功后顺带刷新发现缓存。
            var discovery = await BuildDiscoveryAsync(null, ct);
            StoreCache(null, discovery);
            return new PortainerStatusResponse(
                true,
                BaseUrlOf(_options),
                true,
                discovery.Version,
                discovery.Endpoints.Count,
                true,
                discovery.Self,
                null);
        }
        catch (PortainerException ex)
        {
            _logger.LogWarning(ex, "Portainer 状态检查失败");
            return new PortainerStatusResponse(
                true, BaseUrlOf(_options), false, null, null, true, PortainerSelfIdentity.Empty, ex.Message);
        }
    }

    public async Task<PortainerDiscoveryResponse> GetDiscoveryAsync(int? endpointId, CancellationToken ct)
    {
        if (!_client.IsConfigured)
        {
            throw new PortainerException(_client.NotConfiguredReason ?? "Portainer 未配置", 503);
        }

        var cacheKey = CacheKey(endpointId);
        if (_options.CacheSeconds > 0 &&
            _cache.TryGetValue(cacheKey, out PortainerDiscoveryResponse? cached) &&
            cached is not null)
        {
            return cached;
        }

        var result = await BuildDiscoveryAsync(endpointId, ct);
        StoreCache(endpointId, result);
        return result;
    }

    private static string CacheKey(int? endpointId) =>
        $"portainer:discovery:{endpointId?.ToString() ?? "all"}";

    private static string? BaseUrlOf(PortainerOptions options)
    {
        var value = options.BaseUrl;
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return trimmed;

        // 只展示地址本身，避免 URL 中可能携带的 userinfo 回显到前端。
        var sanitized = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
        return sanitized.Uri.ToString().TrimEnd('/');
    }

    private void StoreCache(int? endpointId, PortainerDiscoveryResponse discovery)
    {
        if (_options.CacheSeconds <= 0) return;
        _cache.Set(CacheKey(endpointId), discovery, TimeSpan.FromSeconds(_options.CacheSeconds));
    }

    private async Task<PortainerDiscoveryResponse> BuildDiscoveryAsync(int? endpointFilter, CancellationToken ct)
    {
        var status = await _client.GetStatusAsync(ct);
        IEnumerable<PortainerEndpointDto> endpoints = await _client.GetEndpointsAsync(ct);

        if (endpointFilter is int filterId)
        {
            endpoints = endpoints.Where(e => e.Id == filterId);
            if (!endpoints.Any())
            {
                throw new PortainerException($"Portainer 中不存在 endpoint {filterId}", 404);
            }
        }

        var results = await Task.WhenAll(endpoints.Select(e => FetchEndpointAsync(e, ct)));

        var self = SelfIdentityResolver.Resolve(
            Environment.MachineName,
            results.Where(r => r.Error is null).SelectMany(r => r.Containers),
            _options.SelfStackNames,
            _options.SelfImagePatterns);

        var endpointViews = new List<PortainerEndpointView>();
        var excludedViews = new List<PortainerExcludedView>();

        foreach (var result in results)
        {
            var endpoint = result.Endpoint;
            if (result.Error is not null)
            {
                endpointViews.Add(new PortainerEndpointView(
                    endpoint.Id,
                    endpoint.Name,
                    EngineOf(endpoint),
                    StatusOf(endpoint),
                    result.Error,
                    []));
                continue;
            }

            var exclusion = SelfExclusionPolicy.Apply(
                result.Containers, self, _options.ExtraExcludedStacks, endpoint.Name);

            excludedViews.AddRange(exclusion.Excluded.Select(e =>
                new PortainerExcludedView(e.Name, e.Reason, e.Stack, e.Endpoint)));

            endpointViews.Add(new PortainerEndpointView(
                endpoint.Id,
                endpoint.Name,
                EngineOf(endpoint),
                StatusOf(endpoint),
                null,
                BuildNetworks(result, exclusion.Included, self)));
        }

        return new PortainerDiscoveryResponse(
            true,
            status.Version,
            PortainerSelfIdentity.From(self),
            endpointViews,
            excludedViews
                .OrderBy(e => e.Endpoint, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private async Task<EndpointResult> FetchEndpointAsync(PortainerEndpointDto endpoint, CancellationToken ct)
    {
        try
        {
            var containersTask = _client.GetContainersAsync(endpoint.Id, ct);
            var networksTask = _client.GetNetworksAsync(endpoint.Id, ct);
            await Task.WhenAll(containersTask, networksTask);

            var drivers = (await networksTask)
                .GroupBy(n => n.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Driver, StringComparer.Ordinal);

            return new EndpointResult(
                endpoint,
                MapContainers(await containersTask),
                drivers,
                null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取 Portainer endpoint {EndpointId} 的容器/网络失败", endpoint.Id);
            var message = ex is PortainerException
                ? ex.Message
                : $"获取容器/网络失败：{ex.Message}";

            return new EndpointResult(
                endpoint,
                [],
                new Dictionary<string, string?>(),
                message);
        }
    }

    private IReadOnlyList<PortainerNetworkView> BuildNetworks(
        EndpointResult result,
        IReadOnlyList<ContainerSnapshot> included,
        SelfIdentity self)
    {
        var importable = included.Where(c => c.Ports.Count > 0).ToList();
        var networkNames = importable
            .SelectMany(c => c.Networks)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var views = new List<PortainerNetworkView>();

        foreach (var networkName in networkNames)
        {
            result.NetworkDrivers.TryGetValue(networkName, out var driver);
            var dataPlaneAttached = DataPlaneDetector.IsAttached(
                result.Containers, networkName, self, _options.DataPlaneService);
            var supportsContainerName = DestinationAddressResolver.SupportsContainerName(networkName, driver);
            var context = new NetworkContext(networkName, driver, dataPlaneAttached);

            var groups = importable
                .Where(c => c.Networks.Contains(networkName, StringComparer.Ordinal))
                .GroupBy(c => c.Stack ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var standalone = group.Key.Length == 0;
                    return new PortainerGroupView(
                        standalone ? StandaloneGroupName : group.Key,
                        standalone ? "standalone" : "compose",
                        group
                            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(c => new PortainerContainerView(
                                c.Id,
                                c.Name,
                                c.Image,
                                c.Service,
                                c.Ports
                                    .OrderBy(p => p.PrivatePort)
                                    .ThenBy(p => p.PublicPort)
                                    .Select(port =>
                                    {
                                        var addresses = DestinationAddressResolver.Resolve(
                                            c, port, context, _options.HostAddress);
                                        return new PortainerPortView(
                                            port.PrivatePort,
                                            port.PublicPort,
                                            port.BindIp,
                                            port.Protocol,
                                            new PortainerAddressView(
                                                addresses.ContainerName,
                                                addresses.Host));
                                    })
                                    .ToList()))
                            .ToList());
                })
                .OrderBy(g => g.Source == "standalone" ? 1 : 0)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groups.Count == 0)
            {
                continue;
            }

            views.Add(new PortainerNetworkView(
                networkName,
                driver,
                string.Equals(networkName, DestinationAddressResolver.DefaultBridgeName, StringComparison.OrdinalIgnoreCase),
                dataPlaneAttached,
                supportsContainerName,
                groups));
        }

        return views
            .OrderBy(v => v.IsDefaultBridge ? 1 : 0)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ContainerSnapshot> MapContainers(IEnumerable<PortainerContainerDto> containers)
    {
        var result = new List<ContainerSnapshot>();

        foreach (var dto in containers)
        {
            var labels = dto.Labels ?? new Dictionary<string, string>();
            var name = dto.Names?
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.TrimStart('/'))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(name))
            {
                name = dto.Id.Length > 12 ? dto.Id[..12] : dto.Id;
            }

            result.Add(new ContainerSnapshot
            {
                Id = dto.Id,
                Name = name,
                State = dto.State ?? string.Empty,
                Image = dto.Image ?? string.Empty,
                Stack = Label(labels, "com.docker.compose.project"),
                Service = Label(labels, "com.docker.compose.service"),
                Networks = dto.NetworkSettings?.Networks?.Keys.ToList() ?? [],
                Ports = MapPorts(dto.Ports),
                IsPortainerSystem =
                    IsTrue(Label(labels, "io.portainer.server")) ||
                    IsTrue(Label(labels, "io.portainer.agent"))
            });
        }

        return result;
    }

    /// <summary>
    /// 仅保留 TCP 端口；同一 private/public 组合的 IPv4/IPv6 通配绑定合并为一条，
    /// 并优先采用具体绑定 IP。
    /// </summary>
    private static List<PortBinding> MapPorts(IEnumerable<PortainerPortDto>? ports)
    {
        if (ports is null) return [];

        var result = new List<PortBinding>();

        var grouped = ports
            .Where(p => p.PrivatePort > 0 && string.Equals(p.Type, "tcp", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => (p.PrivatePort, p.PublicPort));

        foreach (var group in grouped)
        {
            var concrete = group
                .Select(p => p.IP)
                .FirstOrDefault(ip => !DestinationAddressResolver.IsWildcardBind(ip));
            var bindIp = concrete ?? group.Select(p => p.IP).FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip));

            result.Add(new PortBinding(
                group.Key.PrivatePort,
                group.Key.PublicPort,
                bindIp,
                "tcp"));
        }

        return result
            .OrderBy(p => p.PrivatePort)
            .ThenBy(p => p.PublicPort)
            .ToList();
    }

    private static string? Label(IReadOnlyDictionary<string, string> labels, string key) =>
        labels.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string EngineOf(PortainerEndpointDto endpoint) =>
        string.IsNullOrWhiteSpace(endpoint.ContainerEngine) ? "docker" : endpoint.ContainerEngine;

    private static string StatusOf(PortainerEndpointDto endpoint) =>
        endpoint.Status == 1 ? "up" : "down";

    private sealed record EndpointResult(
        PortainerEndpointDto Endpoint,
        IReadOnlyList<ContainerSnapshot> Containers,
        IReadOnlyDictionary<string, string?> NetworkDrivers,
        string? Error);
}
