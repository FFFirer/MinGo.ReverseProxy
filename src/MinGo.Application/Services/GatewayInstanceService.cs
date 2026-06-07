using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;

namespace MinGo.Application.Services;

/// <summary>
/// Gateway实例管理服务 — 内存实现（ConcurrentDictionary），不入数据库
/// </summary>
public class GatewayInstanceService : IGatewayInstanceService
{
    private readonly ConcurrentDictionary<string, GatewayInstance> _instances = new();
    private readonly ILogger<GatewayInstanceService> _logger;

    private static readonly TimeSpan HeartbeatTimeoutThreshold = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromSeconds(120);

    public GatewayInstanceService(ILogger<GatewayInstanceService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 注册Gateway实例
    /// </summary>
    public Task<GatewayInstance> RegisterInstanceAsync(GatewayInstanceRegisterRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var instance = new GatewayInstance
        {
            InstanceId = request.InstanceId ?? Guid.NewGuid().ToString("N")[..8],
            Name = request.Name,
            Version = request.Version,
            IpAddress = request.IpAddress,
            Port = request.Port,
            ListenerAddresses = request.ListenerAddresses,
            Status = GatewayInstanceStatus.Online,
            IsHealthy = true,
            LastHeartbeat = now,
            RegisteredAt = now,
            StartedAt = now,
            MetadataJson = request.Metadata != null ? JsonSerializer.Serialize(request.Metadata) : "{}"
        };

        _instances[instance.InstanceId] = instance;
        _logger.LogInformation("Registered gateway instance: {InstanceId} ({Name})", instance.InstanceId, instance.Name);
        return Task.FromResult(instance);
    }

    /// <summary>
    /// 更新实例心跳
    /// </summary>
    public Task<bool> UpdateHeartbeatAsync(GatewayInstanceHeartbeatRequest request)
    {
        if (!_instances.TryGetValue(request.InstanceId, out var instance))
        {
            _logger.LogDebug("Heartbeat received for unknown instance: {InstanceId}", request.InstanceId);
            return Task.FromResult(false);
        }

        instance.LastHeartbeat = DateTimeOffset.UtcNow;
        instance.CpuUsage = request.CpuUsage;
        instance.MemoryUsage = request.MemoryUsage;
        instance.TotalRequests = request.TotalRequests;
        instance.ErrorRequests = request.ErrorRequests;
        instance.IsHealthy = request.IsHealthy;
        instance.Status = GatewayInstanceStatus.Online;

        // 更新元数据
        if (request.Metadata != null && request.Metadata.Count > 0)
        {
            var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(instance.MetadataJson)
                ?? new Dictionary<string, string>();
            foreach (var (key, val) in request.Metadata)
                existing[key] = val;
            instance.MetadataJson = JsonSerializer.Serialize(existing);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 获取所有实例列表（含超时检测）
    /// </summary>
    public Task<GatewayInstanceListResponse> GetInstancesAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var allInstances = _instances.Values.ToList();

        // 惰性超时检测
        foreach (var inst in allInstances)
        {
            if (inst.Status == GatewayInstanceStatus.Online &&
                now - inst.LastHeartbeat > HeartbeatTimeoutThreshold)
            {
                inst.Status = GatewayInstanceStatus.HeartbeatTimeout;
            }

            if (inst.Status is GatewayInstanceStatus.HeartbeatTimeout or GatewayInstanceStatus.Online &&
                now - inst.LastHeartbeat > OfflineThreshold)
            {
                inst.Status = GatewayInstanceStatus.Offline;
            }
        }

        var response = new GatewayInstanceListResponse
        {
            Instances = allInstances.Select(inst => new GatewayInstanceResponse
            {
                InstanceId = inst.InstanceId,
                Name = inst.Name,
                Version = inst.Version,
                ListenerAddresses = inst.ListenerAddresses,
                Status = inst.Status,
                IsHealthy = inst.IsHealthy,
                LastHeartbeat = inst.LastHeartbeat,
                RegisteredAt = inst.RegisteredAt,
                Uptime = inst.StartedAt.HasValue ? now - inst.StartedAt.Value : TimeSpan.Zero,
                CpuUsage = inst.CpuUsage,
                MemoryUsage = inst.MemoryUsage,
                TotalRequests = inst.TotalRequests,
                ErrorRequests = inst.ErrorRequests,
                ErrorRate = inst.TotalRequests > 0
                    ? Math.Round((double)inst.ErrorRequests / inst.TotalRequests * 100, 2)
                    : 0,
                Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(inst.MetadataJson)
            }).ToList(),
            TotalCount = allInstances.Count,
            OnlineCount = allInstances.Count(i => i.Status == GatewayInstanceStatus.Online),
            OfflineCount = allInstances.Count(i => i.Status == GatewayInstanceStatus.Offline),
            HealthyCount = allInstances.Count(i => i.IsHealthy),
            UnhealthyCount = allInstances.Count(i => !i.IsHealthy)
        };

        return Task.FromResult(response);
    }

    /// <summary>
    /// 获取指定实例
    /// </summary>
    public Task<GatewayInstance?> GetInstanceAsync(string instanceId)
    {
        _instances.TryGetValue(instanceId, out var instance);
        return Task.FromResult(instance);
    }

    /// <summary>
    /// 移除实例
    /// </summary>
    public Task<bool> RemoveInstanceAsync(string instanceId)
    {
        if (_instances.TryRemove(instanceId, out _))
        {
            _logger.LogInformation("Removed gateway instance: {InstanceId}", instanceId);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <summary>
    /// 检查并更新超时实例状态（主动触发）
    /// </summary>
    public Task<int> CheckAndUpdateTimeoutInstancesAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var timeoutCount = 0;

        foreach (var (_, inst) in _instances)
        {
            if (inst.Status == GatewayInstanceStatus.Online &&
                now - inst.LastHeartbeat > HeartbeatTimeoutThreshold)
            {
                inst.Status = GatewayInstanceStatus.HeartbeatTimeout;
                timeoutCount++;
            }

            if (inst.Status is GatewayInstanceStatus.HeartbeatTimeout or GatewayInstanceStatus.Online &&
                now - inst.LastHeartbeat > OfflineThreshold)
            {
                inst.Status = GatewayInstanceStatus.Offline;
                timeoutCount++;
            }
        }

        _logger.LogDebug("Checked timeout instances: {Count} updated", timeoutCount);
        return Task.FromResult(timeoutCount);
    }

    /// <summary>
    /// 清理长时间心跳超时的实例（移除超过 24 小时无心跳且已离线的）
    /// </summary>
    public Task<int> CleanupLongTimeTimeoutInstancesAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - TimeSpan.FromHours(24);
        var removed = 0;

        foreach (var (id, inst) in _instances)
        {
            if (inst.Status == GatewayInstanceStatus.Offline && inst.LastHeartbeat < cutoff)
            {
                if (_instances.TryRemove(id, out _))
                    removed++;
            }
        }

        if (removed > 0)
            _logger.LogInformation("Cleaned up {Count} long-time offline instances", removed);

        return Task.FromResult(removed);
    }
}