using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MinGo.ControlPlane.Api.Models.Portainer;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Portainer;

namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// 容器优先的 Portainer 发现服务：一次枚举 endpoint → 容器 → 网络，
/// 按 compose project 分组，并应用自身识别与排除策略。
/// 支持多实例：每个实例独立发现、独立缓存。
/// </summary>
public sealed class PortainerDiscoveryService
{
    private const string StandaloneGroupName = "(未分组)";

    private readonly PortainerClientFactory _clientFactory;
    private readonly IPortainerInstanceService _instanceService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PortainerDiscoveryService> _logger;

    public PortainerDiscoveryService(
        PortainerClientFactory clientFactory,
        IPortainerInstanceService instanceService,
        IMemoryCache cache,
        ILogger<PortainerDiscoveryService> logger)
    {
        _clientFactory = clientFactory;
        _instanceService = instanceService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PortainerStatusResponse> GetStatusAsync(string instanceId, CancellationToken ct)
    {
        var instance = await _instanceService.GetWithApiKeyAsync(instanceId);
        if (instance is null)
        {
            return new PortainerStatusResponse(
                instanceId,
                false,
                null,
                false,
                null,
                null,
                false,
                PortainerSelfIdentity.Empty,
                $"Portainer 实例 '{instanceId}' 不存在");
        }

        var entity = DtoToEntity(instance);
        var client = _clientFactory.GetOrCreate(entity);

        if (!client.IsConfigured)
        {
            return new PortainerStatusResponse(
                instanceId,
                entity.Enabled,
                SanitizeUrl(entity.BaseUrl),
                false,
                null,
                null,
                !string.IsNullOrWhiteSpace(entity.ApiKey),
                PortainerSelfIdentity.Empty,
                client.NotConfiguredReason);
        }

        try
        {
            var discovery = await BuildDiscoveryAsync(instanceId, entity, client, null, ct);
            StoreCache(instanceId, null, discovery);
            return new PortainerStatusResponse(
                instanceId,
                true,
                SanitizeUrl(entity.BaseUrl),
                true,
                discovery.Version,
                discovery.Endpoints.Count,
                true,
                discovery.Self,
                null);
        }
        catch (PortainerException ex)
        {
            _logger.LogWarning(ex, "Portainer [{InstanceId}] 状态检查失败", instanceId);
            return new PortainerStatusResponse(
                instanceId, true, SanitizeUrl(entity.BaseUrl), false, null, null, true,
                PortainerSelfIdentity.Empty, ex.Message);
        }
    }

    public async Task<PortainerDiscoveryResponse> GetDiscoveryAsync(string instanceId, int? endpointId, CancellationToken ct)
    {
        var instance = await _instanceService.GetWithApiKeyAsync(instanceId);
        if (instance is null)
        {
            throw new PortainerException($"Portainer 实例 '{instanceId}' 不存在", 404);
        }

        var entity = DtoToEntity(instance);
        var client = _clientFactory.GetOrCreate(entity);

        if (!client.IsConfigured)
        {
            throw new PortainerException(client.NotConfiguredReason ?? $"Portainer 实例 '{instanceId}' 未配置", 503);
        }

        var cacheKey = CacheKey(instanceId, endpointId);
        if (entity.CacheSeconds > 0 &&
            _cache.TryGetValue(cacheKey, out PortainerDiscoveryResponse? cached) &&
            cached is not null)
        {
            return cached;
        }

        var result = await BuildDiscoveryAsync(instanceId, entity, client, endpointId, ct);
        StoreCache(instanceId, endpointId, result);
        return result;
    }

    private static string CacheKey(string instanceId, int? endpointId) =>
        $"portainer:discovery:{instanceId}:{endpointId?.ToString() ?? "all"}";

    private static string? SanitizeUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        var trimmed = baseUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return trimmed;
        var sanitized = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
        return sanitized.Uri.ToString().TrimEnd('/');
    }

    private void StoreCache(string instanceId, int? endpointId, PortainerDiscoveryResponse discovery)
    {
        // 使用实例自身的 CacheSeconds（从 entity 获取）
        // 这里简化处理，统一用 30 秒；实际应从实例配置读取
        _cache.Set(CacheKey(instanceId, endpointId), discovery, TimeSpan.FromSeconds(30));
    }

    private async Task<PortainerDiscoveryResponse> BuildDiscoveryAsync(
        string instanceId,
        PortainerInstanceEntity instance,
        PortainerClient client,
        int? endpointFilter,
        CancellationToken ct)
    {
        var status = await client.GetStatusAsync(ct);
        IEnumerable<PortainerEndpointDto> endpoints = await client.GetEndpointsAsync(ct);

        if (endpointFilter is int filterId)
        {
            endpoints = endpoints.Where(e => e.Id == filterId);
            if (!endpoints.Any())
            {
                throw new PortainerException($"Portainer [{instanceId}] 中不存在 endpoint {filterId}", 404);
            }
        }

        var results = await Task.WhenAll(endpoints.Select(e => FetchEndpointAsync(client, e, ct)));

        var self = SelfIdentityResolver.Resolve(
            Environment.MachineName,
            results.Where(r => r.Error is null).SelectMany(r => r.Containers),
            instance.GetSelfStackNames(),
            instance.GetSelfImagePatterns());

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
                result.Containers, self, instance.GetExtraExcludedStacks(), endpoint.Name);

            excludedViews.AddRange(exclusion.Excluded.Select(e =>
                new PortainerExcludedView(e.Name, e.Reason, e.Stack, e.Endpoint)));

            endpointViews.Add(new PortainerEndpointView(
                endpoint.Id,
                endpoint.Name,
                EngineOf(endpoint),
                StatusOf(endpoint),
                null,
                BuildNetworks(instance, result, exclusion.Included, self)));
        }

        return new PortainerDiscoveryResponse(
            instanceId,
            true,
            status.Version,
            PortainerSelfIdentity.From(self),
            endpointViews,
            excludedViews
                .OrderBy(e => e.Endpoint, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private async Task<EndpointResult> FetchEndpointAsync(PortainerClient client, PortainerEndpointDto endpoint, CancellationToken ct)
    {
        try
        {
            var containersTask = client.GetContainersAsync(endpoint.Id, ct);
            var networksTask = client.GetNetworksAsync(endpoint.Id, ct);
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
            _logger.LogWarning(ex, "获取 Portainer [{InstanceId}] endpoint {EndpointId} 的容器/网络失败",
                client.InstanceId, endpoint.Id);
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
        PortainerInstanceEntity instance,
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
                result.Containers, networkName, self, instance.DataPlaneService);
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
                                            c, port, context, instance.HostAddress);
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

    private static PortainerInstanceEntity DtoToEntity(PortainerInstanceDto dto)
    {
        return new PortainerInstanceEntity
        {
            Id = dto.Id,
            Name = dto.Name,
            Enabled = dto.Enabled,
            BaseUrl = dto.BaseUrl,
            ApiKey = dto.ApiKey,
            TimeoutSeconds = dto.TimeoutSeconds,
            CacheSeconds = dto.CacheSeconds,
            HostAddress = dto.HostAddress,
            DataPlaneService = dto.DataPlaneService,
            SelfStackNamesJson = System.Text.Json.JsonSerializer.Serialize(dto.SelfStackNames ?? []),
            SelfImagePatternsJson = System.Text.Json.JsonSerializer.Serialize(dto.SelfImagePatterns ?? ["reverseproxy-cp"]),
            ExtraExcludedStacksJson = System.Text.Json.JsonSerializer.Serialize(dto.ExtraExcludedStacks ?? []),
        };
    }

    private sealed record EndpointResult(
        PortainerEndpointDto Endpoint,
        IReadOnlyList<ContainerSnapshot> Containers,
        IReadOnlyDictionary<string, string?> NetworkDrivers,
        string? Error);
}
