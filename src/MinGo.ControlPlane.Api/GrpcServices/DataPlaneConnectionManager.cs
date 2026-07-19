using System.Collections.Concurrent;
using Grpc.Core;
using MinGo.Core.Entities;
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
    /// 向所有已连接数据面广播配置更新
    /// </summary>
    public async Task BroadcastConfigAsync(ConfigSnapshot snapshot)
    {
        var disconnected = new List<string>();

        foreach (var (id, conn) in _connections)
        {
            try
            {
                await conn.ConfigWriter.WriteAsync(snapshot);
                _logger.LogDebug("Config snapshot version {Version} sent to data plane {Id}", snapshot.Version, id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send config to data plane {Id}, marking disconnected", id);
                disconnected.Add(id);
            }
        }

        foreach (var id in disconnected)
        {
            Unregister(id);
        }
    }

    /// <summary>
    /// 获取指定数据面的状态
    /// </summary>
    public GatewayInstanceStatus? GetStatus(string dataPlaneId)
    {
        if (_connections.TryGetValue(dataPlaneId, out var conn))
        {
            if (DateTimeOffset.UtcNow - conn.LastHeartbeat > TimeSpan.FromSeconds(30))
                return GatewayInstanceStatus.HeartbeatTimeout;
            if (DateTimeOffset.UtcNow - conn.LastHeartbeat > TimeSpan.FromSeconds(120))
                return GatewayInstanceStatus.Offline;
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
    }
}
