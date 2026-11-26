using System.Collections.Concurrent;
using Grpc.Core;
using MinGo.Core.Entities;
using MinGo.Core.Models;
using MinGo.DataPlane.Grpc;

namespace MinGo.ControlPlane.Api.GrpcServices;

/// <summary>
/// 数据面连接管理器 - 跟踪所有已连接的数据面实例
/// </summary>
public class DataPlaneConnectionManager
{
    private readonly ConcurrentDictionary<string, DataPlaneConnection> _connections = new();
    private readonly ILogger<DataPlaneConnectionManager> _logger;

    public DataPlaneConnectionManager(ILogger<DataPlaneConnectionManager> logger)
    {
        _logger = logger;
    }

    public int ConnectedCount => _connections.Count;

    /// <summary>
    /// 注册数据面连接
    /// </summary>
    public void Register(string dataPlaneId, IServerStreamWriter<ConfigSnapshot> configWriter, ServerCallContext context)
    {
        var connection = new DataPlaneConnection
        {
            DataPlaneId = dataPlaneId,
            ConfigWriter = configWriter,
            Context = context,
            ConnectedAt = DateTimeOffset.UtcNow,
            LastHeartbeat = DateTimeOffset.UtcNow
        };

        _connections[dataPlaneId] = connection;
        _logger.LogInformation("Data plane {DataPlaneId} connected. Total connections: {Count}", dataPlaneId, ConnectedCount);
    }

    /// <summary>
    /// 注销数据面连接
    /// </summary>
    public void Unregister(string dataPlaneId)
    {
        if (_connections.TryRemove(dataPlaneId, out _))
        {
            _logger.LogInformation("Data plane {DataPlaneId} disconnected. Total connections: {Count}", dataPlaneId, ConnectedCount);
        }
    }

    /// <summary>
    /// 更新心跳时间
    /// </summary>
    public void UpdateHeartbeat(string dataPlaneId)
    {
        if (_connections.TryGetValue(dataPlaneId, out var conn))
        {
            conn.LastHeartbeat = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// 更新连接的分组归属（分配变更后调用，下一次快照按新分组组装）。
    /// 取消分配回退启动默认组 local。
    /// </summary>
    public bool SetGroup(string dataPlaneId, string? groupId)
    {
        if (!_connections.TryGetValue(dataPlaneId, out var conn))
            return false;

        conn.GroupId = string.IsNullOrEmpty(groupId) ? ConnectionIds.Local : groupId;
        return true;
    }

    /// <summary>
    /// 向指定数据面连接发送配置快照；连接不存在或写入失败（含断开）返回 false。
    /// </summary>
    public async Task<bool> SendConfigAsync(string dataPlaneId, ConfigSnapshot snapshot)
    {
        if (!_connections.TryGetValue(dataPlaneId, out var conn))
            return false;

        try
        {
            await conn.ConfigWriter.WriteAsync(snapshot);
            _logger.LogDebug("Config snapshot version {Version} sent to data plane {Id}", snapshot.Version, dataPlaneId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send config to data plane {Id}, marking disconnected", dataPlaneId);
            Unregister(dataPlaneId);
            return false;
        }
    }

    /// <summary>
    /// 注册数据面心跳响应流（命令下发通道）
    /// </summary>
    public void RegisterHeartbeatWriter(string dataPlaneId, IServerStreamWriter<HeartbeatResponse> writer)
    {
        if (_connections.TryGetValue(dataPlaneId, out var conn))
            conn.HeartbeatWriter = writer;
    }

    /// <summary>
    /// 注销数据面心跳响应流（心跳连接断开后命令通道随之失效）
    /// </summary>
    public void UnregisterHeartbeatWriter(string dataPlaneId)
    {
        if (_connections.TryGetValue(dataPlaneId, out var conn))
            conn.HeartbeatWriter = null;
    }

    /// <summary>
    /// 通过心跳响应流向指定数据面下发控制命令
    /// </summary>
    public async Task<bool> SendCommandAsync(string dataPlaneId, CommandType type, string? payloadJson)
    {
        if (!_connections.TryGetValue(dataPlaneId, out var conn) || conn.HeartbeatWriter == null)
            return false;

        try
        {
            await conn.HeartbeatWriter.WriteAsync(new HeartbeatResponse
            {
                Ack = true,
                Commands = { new ControlCommand { Type = type, PayloadJson = payloadJson ?? "" } }
            });
            _logger.LogInformation("Command {CommandType} sent to data plane {Id}", type, dataPlaneId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send command {CommandType} to data plane {Id}", type, dataPlaneId);
            return false;
        }
    }

    /// <summary>
    /// 获取指定数据面的状态
    /// </summary>
    public GatewayInstanceStatus? GetStatus(string dataPlaneId)
    {
        if (_connections.TryGetValue(dataPlaneId, out var conn))
        {
            var elapsed = DateTimeOffset.UtcNow - conn.LastHeartbeat;
            if (elapsed > TimeSpan.FromSeconds(120))
                return GatewayInstanceStatus.Offline;
            if (elapsed > TimeSpan.FromSeconds(30))
                return GatewayInstanceStatus.HeartbeatTimeout;
            return GatewayInstanceStatus.Online;
        }
        return null;
    }

    /// <summary>
    /// 获取所有连接信息
    /// </summary>
    public IEnumerable<DataPlaneConnection> GetAllConnections() => _connections.Values;

    public class DataPlaneConnection
    {
        public string DataPlaneId { get; set; } = string.Empty;
        public required IServerStreamWriter<ConfigSnapshot> ConfigWriter { get; set; }
        public required ServerCallContext Context { get; set; }
        public DateTimeOffset ConnectedAt { get; set; }
        public DateTimeOffset LastHeartbeat { get; set; }
        public IServerStreamWriter<HeartbeatResponse>? HeartbeatWriter { get; set; }

        /// <summary>
        /// 该连接所属数据面的接入分组 Id（注册时从分配表解析；未分配为启动默认组 local）。
        /// 快照组装与广播都按它过滤分组路由。
        /// </summary>
        public string GroupId { get; set; } = ConnectionIds.Local;
    }
}
