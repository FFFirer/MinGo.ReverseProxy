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

        // 自动注册实例（仅内存）
        var name = $"DataPlane-{subscription.DataPlaneId[..Math.Min(subscription.DataPlaneId.Length, 8)]}";
        await _instanceService.RegisterInstanceAsync(new GatewayInstanceRegisterRequest
        {
            InstanceId = subscription.DataPlaneId,
            Name = name,
            Version = "unknown"
        });

        try
        {
            // 发送全量初始化配置
            var fullSnapshot = await BuildConfigSnapshotAsync(subscription.DataPlaneId);
            await responseStream.WriteAsync(fullSnapshot);
            _logger.LogInformation("Sent full config snapshot (version {Version}) to {DataPlaneId}",
                fullSnapshot.Version, subscription.DataPlaneId);

            // 保持连接，等待后续推送
            // 当服务端有配置变更时，通过 BroadcastConfigAsync 推送
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
    /// 从数据库构建全量配置快照
    /// </summary>
    private async Task<ConfigSnapshot> BuildConfigSnapshotAsync(string dataPlaneId)
    {
        using var scope = _scopeFactory.CreateScope();
        var apiDbService = scope.ServiceProvider.GetRequiredService<IApiDbService>();

        var routes = await apiDbService.GetRoutesAsync();
        var clusters = await apiDbService.GetClustersAsync();

        var snapshot = new ConfigSnapshot
        {
            Version = _versionCounter.Next(),
            UpdateType = UpdateType.FullSync,
            Checksum = Guid.NewGuid().ToString("N")[..16]
        };

        AddRoutesToSnapshot(snapshot, routes);

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

        // 添加证书
        await AddCertificatesToSnapshotAsync(apiDbService, snapshot);

        return snapshot;
    }

    /// <summary>
    /// 把路由表编译成下发给数据面的规则集（全量快照与增量广播共用一条口径）。
    /// </summary>
    /// <remarks>
    /// 这里是「外部隧道不直连后端」契约的另一半：隧道 ingress 把所有托管域名统一回源到数据面，
    /// 所以绑定隧道的启用路由必须同样产出一条「数据面 → 后端集群」规则，否则隧道流量落地即 404。
    /// 编译刻意不读 <c>ConnectionId</c>——接入通道只决定流量从哪里进来，不决定规则装在哪个进程；
    /// 想改成按通道过滤，先过 <c>DataPlaneRuleCompilerTests.Compile_KeepsTunnelBoundRoutes</c> 这一关。
    /// </remarks>
    private void AddRoutesToSnapshot(ConfigSnapshot snapshot, IEnumerable<ModelRoute> routes)
    {
        var plan = DataPlaneRuleCompiler.Compile(routes);

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
    }

    /// <summary>
    /// 将证书数据添加到 ConfigSnapshot
    /// </summary>
    /// <remarks>
    /// 只剔除不可加载的记录（无字节、无域名），每个域名一张；过期证书照常下发，
    /// 否则 <c>enable-https-redirect</c> 的 fail-closed 会退化成明文回退。
    /// </remarks>
    private static async Task AddCertificatesToSnapshotAsync(IApiDbService apiDbService, ConfigSnapshot snapshot)
    {
        var certificates = await apiDbService.GetCertificatesAsync();
        foreach (var cert in CertificateSnapshotPolicy.SelectForDelivery(certificates, DateTimeOffset.UtcNow))
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
    /// 广播配置更新到所有已连接数据面
    /// </summary>
    public async Task BroadcastConfigUpdateAsync()
    {
        if (_connectionManager.ConnectedCount == 0)
        {
            _logger.LogDebug("No connected data planes to broadcast config update");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var apiDbService = scope.ServiceProvider.GetRequiredService<IApiDbService>();

        var routes = await apiDbService.GetRoutesAsync();
        var clusters = await apiDbService.GetClustersAsync();

        var snapshot = new ConfigSnapshot
        {
            Version = _versionCounter.Next(),
            UpdateType = UpdateType.FullSync,
            Checksum = Guid.NewGuid().ToString("N")[..16]
        };

        AddRoutesToSnapshot(snapshot, routes);

        foreach (var cluster in clusters)
        {
            var cc = new ClusterConfig
            {
                Id = cluster.Id,
                LoadBalancingPolicy = cluster.LoadBalancingPolicy
            };
            if (cluster.Destinations != null)
            {
                foreach (var dest in cluster.Destinations)
                {
                    cc.Destinations.Add(new DestinationConfig
                    {
                        Id = dest.Id,
                        Address = dest.Address,
                        Healthy = dest.Healthy
                    });
                }
            }
            snapshot.Clusters.Add(cc);
        }

        // 广播时也包含证书数据
        await AddCertificatesToSnapshotAsync(apiDbService, snapshot);

        await _connectionManager.BroadcastConfigAsync(snapshot);
        _logger.LogInformation("Broadcast config update version {Version} to {Count} data planes",
            snapshot.Version, _connectionManager.ConnectedCount);
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
