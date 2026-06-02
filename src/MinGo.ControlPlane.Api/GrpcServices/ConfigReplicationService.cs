using Grpc.Core;
using MinGo.Core.Interfaces;
using MinGo.DataPlane.Grpc;
using MinGo.Infrastructure.Data;

namespace MinGo.ControlPlane.Api.GrpcServices;

/// <summary>
/// 配置复制 gRPC 服务 - 向数据面推送配置更新
/// </summary>
public class ConfigReplicationService : ConfigReplication.ConfigReplicationBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataPlaneConnectionManager _connectionManager;
    private readonly ILogger<ConfigReplicationService> _logger;

    public ConfigReplicationService(
        IServiceScopeFactory scopeFactory,
        DataPlaneConnectionManager connectionManager,
        ILogger<ConfigReplicationService> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
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

        // 注册连接
        _connectionManager.Register(subscription.DataPlaneId, responseStream, context);

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
        var certificates = await apiDbService.GetCertificatesAsync();

        var snapshot = new ConfigSnapshot
        {
            Version = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdateType = UpdateType.FullSync,
            Checksum = Guid.NewGuid().ToString("N")[..16]
        };

        foreach (var route in routes)
        {
            snapshot.Routes.Add(new RouteConfig
            {
                Id = route.Id,
                Name = route.Name,
                ClusterId = route.ClusterId,
                MatchPath = route.Match?.Path ?? "",
                MatchHost = route.Match?.Host ?? "",
                TransformsJson = System.Text.Json.JsonSerializer.Serialize(route.Transforms),
                Enabled = route.Enabled
            });
        }

        foreach (var cluster in clusters)
        {
            var clusterConfig = new ClusterConfig
            {
                Id = cluster.Id,
                Name = cluster.Name,
                LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                HealthCheckJson = System.Text.Json.JsonSerializer.Serialize(cluster.HealthCheck)
            };

            if (cluster.Destinations != null)
            {
                foreach (var (destId, dest) in cluster.Destinations)
                {
                    clusterConfig.Destinations.Add(new DestinationConfig
                    {
                        Id = destId,
                        Address = dest.Address,
                        Healthy = dest.Healthy
                    });
                }
            }

            snapshot.Clusters.Add(clusterConfig);
        }

        // 添加证书
        foreach (var cert in certificates)
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

        return snapshot;
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
            Version = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdateType = UpdateType.FullSync,
            Checksum = Guid.NewGuid().ToString("N")[..16]
        };

        foreach (var route in routes)
        {
            snapshot.Routes.Add(new RouteConfig
            {
                Id = route.Id,
                Name = route.Name,
                ClusterId = route.ClusterId,
                MatchPath = route.Match?.Path ?? "",
                Enabled = route.Enabled
            });
        }

        foreach (var cluster in clusters)
        {
            var cc = new ClusterConfig
            {
                Id = cluster.Id,
                Name = cluster.Name,
                LoadBalancingPolicy = cluster.LoadBalancingPolicy
            };
            if (cluster.Destinations != null)
            {
                foreach (var (did, dest) in cluster.Destinations)
                {
                    cc.Destinations.Add(new DestinationConfig
                    {
                        Id = did,
                        Address = dest.Address,
                        Healthy = dest.Healthy
                    });
                }
            }
            snapshot.Clusters.Add(cc);
        }

        await _connectionManager.BroadcastConfigAsync(snapshot);
        _logger.LogInformation("Broadcast config update version {Version} to {Count} data planes",
            snapshot.Version, _connectionManager.ConnectedCount);
    }
}
