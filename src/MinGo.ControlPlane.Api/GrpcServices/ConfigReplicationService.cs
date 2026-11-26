using Grpc.Core;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Routing;
using MinGo.DataPlane.Grpc;
using MinGo.Infrastructure.Data;
using CertificateSnapshotPolicy = MinGo.Core.Models.CertificateSnapshotPolicy;
using ModelRoute = MinGo.Core.Models.RouteConfig;

namespace MinGo.ControlPlane.Api.GrpcServices;

/// <summary>
/// 配置复制 gRPC 服务 - 向数据面推送配置更新
/// </summary>
public class ConfigReplicationService : ConfigReplication.ConfigReplicationBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataPlaneConnectionManager _connectionManager;
    private readonly IGatewayInstanceService _instanceService;
    private readonly ILogger<ConfigReplicationService> _logger;
    private readonly MonotonicVersionCounter _versionCounter = new();

    public ConfigReplicationService(
        IServiceScopeFactory scopeFactory,
        DataPlaneConnectionManager connectionManager,
        IGatewayInstanceService instanceService,
        ILogger<ConfigReplicationService> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
        _instanceService = instanceService;
        _logger = logger;
    }

    public override async Task ReplicateConfig(
        IAsyncStreamReader<ConfigSubscription> requestStream,
        IServerStreamWriter<ConfigSnapshot> responseStream,
        ServerCallContext context)
    {
        // 读取客户端订阅请求
        if (!await requestStream.MoveNext(context.CancellationToken))
            return;

        var subscription = requestStream.Current;
        _logger.LogInformation("Data plane {DataPlaneId} subscribed (version: {Version})",
            subscription.DataPlaneId, subscription.CurrentConfigVersion);

        // 注册 gRPC 连接
        _connectionManager.Register(subscription.DataPlaneId, responseStream, context);

        // 自动注册实例（仅内存）。名称/版本/监听地址来自 Agent 订阅自描述，
        // 老数据面不填则回退到占位值；IP 取 gRPC 连接对端。
        var remoteIp = context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        await _instanceService.RegisterInstanceAsync(new GatewayInstanceRegisterRequest
        {
            InstanceId = subscription.DataPlaneId,
            Name = string.IsNullOrWhiteSpace(subscription.Name)
                ? $"DataPlane-{subscription.DataPlaneId[..Math.Min(subscription.DataPlaneId.Length, 8)]}"
                : subscription.Name,
            Version = string.IsNullOrWhiteSpace(subscription.Version) ? "unknown" : subscription.Version,
            IpAddress = remoteIp ?? string.Empty,
            ListenerAddresses = subscription.ListenerAddresses.Count > 0 ? subscription.ListenerAddresses.ToList() : null
        });

        // 从落库的分配表解析节点归属，同步到连接与内存实例。
        // 未绑定节点的实例不推送任何配置（fail-closed）；绑定后立即按节点组装推送。
        string? nodeId = null;
        string? nodeName = null;
        using (var resolveScope = _scopeFactory.CreateScope())
        {
            var apiDbService = resolveScope.ServiceProvider.GetRequiredService<IApiDbService>();
            var assignment = await apiDbService.GetInstanceNodeAsync(subscription.DataPlaneId);
            if (assignment != null)
            {
                nodeId = assignment.NodeId;
                nodeName = assignment.NodeName;
            }
        }
        _connectionManager.SetNode(subscription.DataPlaneId, nodeId);
        await _instanceService.UpdateInstanceNodeAsync(subscription.DataPlaneId, nodeId, nodeName);

        try
        {
            if (nodeId == null)
            {
                // 未绑定节点：不发送初始化快照，实例不生效任何转发规则。
                // 连接保持存活（心跳/命令通道可用），绑定节点后由绑定流程重推快照。
                _logger.LogInformation(
                    "Data plane {DataPlaneId} is not bound to any node, no config pushed",
                    subscription.DataPlaneId);
            }
            else
            {
                var fullSnapshot = await BuildConfigSnapshotAsync(nodeId);
                await responseStream.WriteAsync(fullSnapshot);
                _logger.LogInformation("Sent full config snapshot (version {Version}) to {DataPlaneId}",
                    fullSnapshot.Version, subscription.DataPlaneId);
            }

            // 保持连接，等待后续推送
            // 当服务端有配置变更时，通过 BroadcastConfigUpdateAsync 按分组逐连接推送
            // 客户端断开时，MoveNext 会返回 false 或抛出异常
            try
            {
                await requestStream.MoveNext(context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 客户端正常断开
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Data plane {DataPlaneId} stream ended", subscription.DataPlaneId);
            }
        }
        finally
        {
            _connectionManager.Unregister(subscription.DataPlaneId);

            // 标记实例为离线
            var instance = await _instanceService.GetInstanceAsync(subscription.DataPlaneId);
            if (instance != null)
            {
                instance.Status = GatewayInstanceStatus.Offline;
                _logger.LogInformation("Data plane {DataPlaneId} marked as Offline", subscription.DataPlaneId);
            }
        }
    }

    /// <summary>
    /// 从数据库构建全量配置快照。
    /// </summary>
    /// <param name="targetNodeId">
    /// 目标数据面所属节点 Id；null 表示未绑定（解绑清空场景）——规则集与证书均为空。
    /// 节点路由只对匹配节点生效，隧道路由对所有已绑定节点的数据面生效。
    /// </param>
    private async Task<ConfigSnapshot> BuildConfigSnapshotAsync(string? targetNodeId)
    {
        using var scope = _scopeFactory.CreateScope();
        var apiDbService = scope.ServiceProvider.GetRequiredService<IApiDbService>();
        return await BuildConfigSnapshotAsync(apiDbService, targetNodeId);
    }

    /// <summary>
    /// 快照组装核心：路由按目标节点过滤，集群全量下发，证书按快照规则引用的域名过滤。
    /// 全量初始化与逐连接广播共用这一条口径。
    /// </summary>
    private async Task<ConfigSnapshot> BuildConfigSnapshotAsync(IApiDbService apiDbService, string? targetNodeId)
    {
        var routes = await apiDbService.GetRoutesAsync();
        var clusters = await apiDbService.GetClustersAsync();

        var snapshot = new ConfigSnapshot
        {
            Version = _versionCounter.Next(),
            UpdateType = UpdateType.FullSync,
            Checksum = Guid.NewGuid().ToString("N")[..16]
        };

        var plan = AddRoutesToSnapshot(snapshot, routes, targetNodeId);

        foreach (var cluster in clusters)
        {
            var clusterConfig = new ClusterConfig
            {
                Id = cluster.Id,
                LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                HealthCheckJson = System.Text.Json.JsonSerializer.Serialize(cluster.HealthCheck)
            };

            if (cluster.Destinations != null)
            {
                foreach (var dest in cluster.Destinations)
                {
                    clusterConfig.Destinations.Add(new DestinationConfig
                    {
                        Id = dest.Id,
                        Address = dest.Address,
                        Healthy = dest.Healthy
                    });
                }
            }

            snapshot.Clusters.Add(clusterConfig);
        }

        // 证书只下发快照规则引用的域名（节点隔离下私钥不跨节点）；空域名规则视为任意域名，
        // 由 CertificateSnapshotPolicy 决定回退到全量下发。空规则集（解绑空快照）同样产出空证书
        // ——SelectForDelivery 对空 routeHosts 的 All 判定为真，hosts 为空集，全量证书被过滤。
        var routeHosts = plan.Rules.Select(r => r.Match?.Host ?? string.Empty).ToList();
        await AddCertificatesToSnapshotAsync(apiDbService, snapshot, routeHosts);

        return snapshot;
    }

    /// <summary>
    /// 把路由表编译成下发给数据面的规则集（全量快照与增量广播共用一条口径）。
    /// </summary>
    /// <remarks>
    /// 这里是「外部隧道不直连后端」契约的另一半：隧道 ingress 把所有托管域名统一回源到数据面，
    /// 所以绑定隧道的启用路由必须同样产出一条「数据面 → 后端集群」规则，否则隧道流量落地即 404。
    /// 编译只按 <c>node:&lt;NodeId&gt;</c> 过滤节点路由，隧道路由对所有已绑定节点的数据面生效；
    /// 未绑定节点（targetNodeId 为 null）产出空规则集。想改动过滤口径，
    /// 先过 <c>DataPlaneRuleCompilerTests</c> 的契约测试这一关。
    /// </remarks>
    private DataPlaneRuleCompiler.Result AddRoutesToSnapshot(
        ConfigSnapshot snapshot, IEnumerable<ModelRoute> routes, string? targetNodeId)
    {
        var plan = DataPlaneRuleCompiler.Compile(routes, targetNodeId);

        foreach (var warning in plan.Warnings)
        {
            _logger.LogWarning("数据面规则去重：{Warning}", warning);
        }

        foreach (var route in plan.Rules)
        {
            snapshot.Routes.Add(new RouteConfig
            {
                Id = route.Id,
                Name = route.Name,
                ClusterId = route.ClusterId,
                MatchPath = route.Match?.Path ?? "",
                MatchHost = route.Match?.Host ?? "",
                TransformsJson = System.Text.Json.JsonSerializer.Serialize(route.Transforms ?? new List<Dictionary<string, string>>()),
                Enabled = route.Enabled
            });
        }

        return plan;
    }

    /// <summary>
    /// 将证书数据添加到 ConfigSnapshot
    /// </summary>
    /// <remarks>
    /// 只剔除不可加载的记录（无字节、无域名），每个域名一张；过期证书照常下发，
    /// 否则 <c>enable-https-redirect</c> 的 fail-closed 会退化成明文回退。
    /// <paramref name="routeHosts"/> 非空时只下发与快照规则域名有交集的证书（节点隔离，私钥不跨节点）。
    /// </remarks>
    private static async Task AddCertificatesToSnapshotAsync(
        IApiDbService apiDbService, ConfigSnapshot snapshot, IReadOnlyCollection<string>? routeHosts)
    {
        var certificates = await apiDbService.GetCertificatesAsync();
        foreach (var cert in CertificateSnapshotPolicy.SelectForDelivery(certificates, DateTimeOffset.UtcNow, routeHosts))
        {
            snapshot.Certificates.Add(new CertificateData
            {
                DomainName = cert.DomainName,
                CertificateType = cert.CertificateType,
                CertificateBytes = Google.Protobuf.ByteString.CopyFrom(cert.CertificateData ?? Array.Empty<byte>()),
                Password = cert.Password ?? "",
                Thumbprint = cert.Thumbprint,
                IsValid = cert.IsValid,
                ExpiresAtUnixMs = cert.ExpiresAt?.ToUnixTimeMilliseconds() ?? 0
            });
        }
    }

    /// <summary>
    /// 广播配置更新到所有已连接数据面：同一节点的连接共享一份快照，
    /// 未绑定节点的连接被跳过（不推送任何配置）。
    /// </summary>
    public async Task BroadcastConfigUpdateAsync()
    {
        var connections = _connectionManager.GetAllConnections().ToList();
        if (connections.Count == 0)
        {
            _logger.LogDebug("No connected data planes to broadcast config update");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var apiDbService = scope.ServiceProvider.GetRequiredService<IApiDbService>();

        var sent = 0;
        foreach (var node in connections
                     .Where(c => !string.IsNullOrEmpty(c.NodeId))
                     .GroupBy(c => c.NodeId, StringComparer.Ordinal))
        {
            var snapshot = await BuildConfigSnapshotAsync(apiDbService, node.Key);

            foreach (var conn in node)
            {
                if (await _connectionManager.SendConfigAsync(conn.DataPlaneId, snapshot))
                    sent++;
            }
        }

        _logger.LogInformation("Broadcast config update to {Count} data planes", sent);
    }

    /// <summary>
    /// 向指定数据面重推配置（实例节点绑定变更时调用）。实例未连接时返回 false。
    /// 未绑定节点时推送空快照（无路由、无证书），清除数据面残留的旧规则。
    /// </summary>
    public async Task<bool> BroadcastConfigToDataPlaneAsync(string dataPlaneId)
    {
        var conn = _connectionManager.GetAllConnections().FirstOrDefault(c => c.DataPlaneId == dataPlaneId);
        if (conn == null)
            return false;

        var targetNodeId = string.IsNullOrEmpty(conn.NodeId) ? null : conn.NodeId;
        var snapshot = await BuildConfigSnapshotAsync(targetNodeId);
        return await _connectionManager.SendConfigAsync(dataPlaneId, snapshot);
    }
}

/// <summary>
/// 单调递增版本号计数器，确保同一秒内多次广播不会产生重复版本号
/// </summary>
internal sealed class MonotonicVersionCounter
{
    private int _current;

    public int Next()
    {
        var timestampBased = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int original, next;
        do
        {
            original = Volatile.Read(ref _current);
            next = Math.Max(timestampBased, original + 1);
        } while (Interlocked.CompareExchange(ref _current, next, original) != original);
        return next;
    }
}
