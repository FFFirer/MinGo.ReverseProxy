using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MinGo.ControlPlane.Data;
using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Services;

/// <summary>
/// Gateway实例管理服务实现
/// </summary>
public class GatewayInstanceService : IGatewayInstanceService
{
    private readonly GatewayDbContext _dbContext;
    private readonly ILogger<GatewayInstanceService> _logger;
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _cleanupTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="dbContext">数据库上下文</param>
    /// <param name="logger">日志记录器</param>
    public GatewayInstanceService(GatewayDbContext dbContext, ILogger<GatewayInstanceService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 注册Gateway实例
    /// </summary>
    /// <param name="request">注册请求</param>
    /// <returns>注册结果</returns>
    public async Task<GatewayInstance> RegisterInstanceAsync(GatewayInstanceRegisterRequest request)
    {
        var instanceId = request.InstanceId ?? Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        var existingInstance = await _dbContext.GatewayInstances
            .FirstOrDefaultAsync(i => i.InstanceId == instanceId);

        if (existingInstance != null)
        {
            existingInstance.Name = request.Name;
            existingInstance.Version = request.Version;
            existingInstance.IpAddress = request.IpAddress;
            existingInstance.Port = request.Port;
            existingInstance.ListenerAddressesJson = JsonSerializer.Serialize(request.ListenerAddresses ?? new List<string>());
            existingInstance.Status = (int)GatewayInstanceStatus.Online;
            existingInstance.IsHealthy = true;
            existingInstance.LastHeartbeat = now;
            existingInstance.StartedAt = now;
            existingInstance.MetadataJson = JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, string>());
            existingInstance.CpuUsage = 0;
            existingInstance.MemoryUsage = 0;
            existingInstance.TotalRequests = 0;
            existingInstance.ErrorRequests = 0;

            _dbContext.GatewayInstances.Update(existingInstance);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Gateway instance {InstanceId} re-registered successfully", instanceId);

            return MapToModel(existingInstance);
        }

        var entity = new GatewayInstanceEntity
        {
            InstanceId = instanceId,
            Name = request.Name,
            Version = request.Version,
            IpAddress = request.IpAddress,
            Port = request.Port,
            ListenerAddressesJson = JsonSerializer.Serialize(request.ListenerAddresses ?? new List<string>()),
            Status = (int)GatewayInstanceStatus.Online,
            IsHealthy = true,
            LastHeartbeat = now,
            RegisteredAt = now,
            StartedAt = now,
            MetadataJson = JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, string>()),
            CpuUsage = 0,
            MemoryUsage = 0,
            TotalRequests = 0,
            ErrorRequests = 0
        };

        _dbContext.GatewayInstances.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Gateway instance {InstanceId} registered successfully at {Address}:{Port}",
            instanceId, request.IpAddress, request.Port);

        return MapToModel(entity);
    }

    /// <summary>
    /// 更新实例心跳
    /// </summary>
    /// <param name="request">心跳请求</param>
    /// <returns>是否成功</returns>
    public async Task<bool> UpdateHeartbeatAsync(GatewayInstanceHeartbeatRequest request)
    {
        var instance = await _dbContext.GatewayInstances
            .FirstOrDefaultAsync(i => i.InstanceId == request.InstanceId);

        if (instance == null)
        {
            _logger.LogWarning("Heartbeat from unknown instance {InstanceId}", request.InstanceId);
            return false;
        }

        instance.LastHeartbeat = DateTimeOffset.UtcNow;
        instance.CpuUsage = request.CpuUsage;
        instance.MemoryUsage = request.MemoryUsage;
        instance.TotalRequests = request.TotalRequests;
        instance.ErrorRequests = request.ErrorRequests;
        instance.IsHealthy = request.IsHealthy;
        instance.Status = (int)(request.IsHealthy ? GatewayInstanceStatus.Online : GatewayInstanceStatus.Unhealthy);

        if (request.Metadata != null && request.Metadata.Count > 0)
        {
            instance.MetadataJson = JsonSerializer.Serialize(request.Metadata);
        }

        await _dbContext.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// 获取所有在线实例
    /// </summary>
    /// <returns>实例列表响应</returns>
    public async Task<GatewayInstanceListResponse> GetInstancesAsync()
    {
        var instances = await _dbContext.GatewayInstances
            .ToListAsync();

        instances = instances
            .OrderBy(i => i.RegisteredAt)
            .ToList();

        var response = new GatewayInstanceListResponse
        {
            Instances = instances.Select(MapToResponse).ToList(),
            TotalCount = instances.Count,
            OnlineCount = instances.Count(i => i.Status == (int)GatewayInstanceStatus.Online),
            OfflineCount = instances.Count(i => i.Status == (int)GatewayInstanceStatus.Offline),
            HealthyCount = instances.Count(i => i.IsHealthy),
            UnhealthyCount = instances.Count(i => !i.IsHealthy)
        };

        return response;
    }

    /// <summary>
    /// 获取指定实例
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <returns>实例信息</returns>
    public async Task<GatewayInstance?> GetInstanceAsync(string instanceId)
    {
        var entity = await _dbContext.GatewayInstances
            .FirstOrDefaultAsync(i => i.InstanceId == instanceId);

        return entity == null ? null : MapToModel(entity);
    }

    /// <summary>
    /// 移除实例
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <returns>是否成功</returns>
    public async Task<bool> RemoveInstanceAsync(string instanceId)
    {
        var entity = await _dbContext.GatewayInstances
            .FirstOrDefaultAsync(i => i.InstanceId == instanceId);

        if (entity == null)
        {
            return false;
        }

        entity.Status = (int)GatewayInstanceStatus.Offline;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Gateway instance {InstanceId} removed", instanceId);
        return true;
    }

    /// <summary>
    /// 检查并更新超时实例状态
    /// </summary>
    /// <returns>超时实例数量</returns>
    public async Task<int> CheckAndUpdateTimeoutInstancesAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var timeoutThreshold = now - _heartbeatTimeout;

            _logger.LogInformation("Checking for timeout instances with threshold: {Threshold}", timeoutThreshold);

            // 先获取所有实例，然后在内存中过滤
            var allInstances = await _dbContext.GatewayInstances.ToListAsync();
            _logger.LogInformation("Found {Count} total instances", allInstances.Count);

            var timeoutInstances = allInstances
                .Where(i => i.Status == (int)GatewayInstanceStatus.Online && i.LastHeartbeat < timeoutThreshold)
                .ToList();

            _logger.LogInformation("Found {Count} timeout instances", timeoutInstances.Count);

            foreach (var instance in timeoutInstances)
            {
                instance.Status = (int)GatewayInstanceStatus.HeartbeatTimeout;
                _logger.LogWarning("Gateway instance {InstanceId} heartbeat timeout", instance.InstanceId);
            }

            if (timeoutInstances.Count > 0)
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Updated {Count} instances to heartbeat timeout status", timeoutInstances.Count);
            }

            return timeoutInstances.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking and updating timeout instances");
            return 0;
        }
    }

    /// <summary>
    /// 清理长时间心跳超时的实例
    /// </summary>
    /// <returns>清理的实例数量</returns>
    public async Task<int> CleanupLongTimeTimeoutInstancesAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var cleanupThreshold = now - _cleanupTimeout;

            _logger.LogInformation("Checking for long time timeout instances with threshold: {Threshold}", cleanupThreshold);

            // 先获取所有实例，然后在内存中过滤
            var allInstances = await _dbContext.GatewayInstances.ToListAsync();
            _logger.LogInformation("Found {Count} total instances", allInstances.Count);

            var timeoutInstances = allInstances
                .Where(i => i.Status == (int)GatewayInstanceStatus.HeartbeatTimeout && i.LastHeartbeat < cleanupThreshold)
                .ToList();

            _logger.LogInformation("Found {Count} long time timeout instances to clean up", timeoutInstances.Count);

            foreach (var instance in timeoutInstances)
            {
                _dbContext.GatewayInstances.Remove(instance);
                _logger.LogInformation("Gateway instance {InstanceId} cleaned up due to long time heartbeat timeout", instance.InstanceId);
            }

            if (timeoutInstances.Count > 0)
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Cleaned up {Count} instances with long time heartbeat timeout", timeoutInstances.Count);
            }

            return timeoutInstances.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up long time timeout instances");
            return 0;
        }
    }

    /// <summary>
    /// 将实体转换为模型
    /// </summary>
    /// <param name="entity">数据库实体</param>
    /// <returns>领域模型</returns>
    private static GatewayInstance MapToModel(GatewayInstanceEntity entity)
    {
        List<string>? listenerAddresses = null;
        try
        {
            if (!string.IsNullOrEmpty(entity.ListenerAddressesJson) && entity.ListenerAddressesJson != "[]")
            {
                listenerAddresses = JsonSerializer.Deserialize<List<string>>(entity.ListenerAddressesJson);
            }
        }
        catch
        {
            listenerAddresses = null;
        }

        return new GatewayInstance
        {
            InstanceId = entity.InstanceId,
            Name = entity.Name,
            Version = entity.Version,
            IpAddress = entity.IpAddress,
            Port = entity.Port,
            ListenerAddresses = listenerAddresses,
            Status = (GatewayInstanceStatus)entity.Status,
            IsHealthy = entity.IsHealthy,
            LastHeartbeat = entity.LastHeartbeat,
            RegisteredAt = entity.RegisteredAt,
            StartedAt = entity.StartedAt,
            MetadataJson = entity.MetadataJson,
            CpuUsage = entity.CpuUsage,
            MemoryUsage = entity.MemoryUsage,
            TotalRequests = entity.TotalRequests,
            ErrorRequests = entity.ErrorRequests
        };
    }

    /// <summary>
    /// 将实体转换为响应对象
    /// </summary>
    /// <param name="entity">数据库实体</param>
    /// <returns>API响应</returns>
    private static GatewayInstanceResponse MapToResponse(GatewayInstanceEntity entity)
    {
        var now = DateTimeOffset.UtcNow;
        var uptime = entity.StartedAt.HasValue ? now - entity.StartedAt.Value : TimeSpan.Zero;
        var errorRate = entity.TotalRequests > 0 ? (double)entity.ErrorRequests / entity.TotalRequests : 0;

        Dictionary<string, string>? metadata = null;
        try
        {
            if (!string.IsNullOrEmpty(entity.MetadataJson) && entity.MetadataJson != "{}")
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.MetadataJson);
            }
        }
        catch
        {
            metadata = null;
        }

        List<string>? listenerAddresses = null;
        try
        {
            if (!string.IsNullOrEmpty(entity.ListenerAddressesJson) && entity.ListenerAddressesJson != "[]")
            {
                listenerAddresses = JsonSerializer.Deserialize<List<string>>(entity.ListenerAddressesJson);
            }
        }
        catch
        {
            listenerAddresses = null;
        }

        return new GatewayInstanceResponse
        {
            InstanceId = entity.InstanceId,
            Name = entity.Name,
            Version = entity.Version,
            Address = listenerAddresses?.FirstOrDefault() ?? $"{entity.IpAddress}:{entity.Port}",
            ListenerAddresses = listenerAddresses,
            Status = (GatewayInstanceStatus)entity.Status,
            IsHealthy = entity.IsHealthy,
            LastHeartbeat = entity.LastHeartbeat,
            RegisteredAt = entity.RegisteredAt,
            Uptime = uptime,
            CpuUsage = entity.CpuUsage,
            MemoryUsage = entity.MemoryUsage,
            TotalRequests = entity.TotalRequests,
            ErrorRequests = entity.ErrorRequests,
            ErrorRate = errorRate,
            Metadata = metadata
        };
    }
}
