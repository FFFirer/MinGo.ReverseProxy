using Grpc.Core;
using Microsoft.Extensions.Logging;
using MinGo.DataPlane.Grpc;
using MinGo.DataPlane.Kestrel;

namespace MinGo.DataPlane.ConfigSync;

/// <summary>
/// 配置同步服务 - 通过 gRPC 双向流从控制面同步配置
/// </summary>
public class ConfigSyncService : IDisposable
{
    private readonly ConfigReplication.ConfigReplicationClient _client;
    private readonly DataPlaneConfigProvider _configProvider;
    private readonly DataPlaneCertificateSelector _certSelector;
    private readonly ILogger<ConfigSyncService> _logger;
    private readonly string _dataPlaneId;
    private AsyncDuplexStreamingCall<ConfigSubscription, ConfigSnapshot>? _call;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _disposed;
    private bool _initialConfigReceived;

    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)];

    public ConfigSyncService(
        ConfigReplication.ConfigReplicationClient client,
        DataPlaneConfigProvider configProvider,
        DataPlaneCertificateSelector certSelector,
        ILogger<ConfigSyncService> logger)
    {
        _client = client;
        _configProvider = configProvider;
        _certSelector = certSelector;
        _logger = logger;
        _dataPlaneId = Guid.NewGuid().ToString("N")[..8];
    }

    public string DataPlaneId => _dataPlaneId;

    public bool InitialConfigReceived => _initialConfigReceived;

    /// <summary>
    /// 启动配置同步（建立 gRPC 双向流）
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = RunSyncLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 等待首次配置到达
    /// </summary>
    public async Task<bool> WaitForInitialConfigAsync(TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!_initialConfigReceived && DateTime.UtcNow - start < timeout)
        {
            await Task.Delay(100);
        }
        return _initialConfigReceived;
    }

    private async Task RunSyncLoopAsync(CancellationToken ct)
    {
        var retryIndex = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting to control plane gRPC ConfigReplication...");

                _call = _client.ReplicateConfig(cancellationToken: ct);

                // 发送订阅请求
                await _call.RequestStream.WriteAsync(new ConfigSubscription
                {
                    DataPlaneId = _dataPlaneId,
                    CurrentConfigVersion = _configProvider.CurrentVersion,
                    SubscribeCertificates = true
                });

                _logger.LogInformation("Connected to control plane, waiting for config...");
                retryIndex = 0;

                // 持续接收配置更新
                await foreach (var snapshot in _call.ResponseStream.ReadAllAsync(ct))
                {
                    await ApplyConfigSnapshotAsync(snapshot);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Config sync connection lost, retrying in {Delay}...",
                    RetryDelays[Math.Min(retryIndex, RetryDelays.Length - 1)]);

                await Task.Delay(RetryDelays[Math.Min(retryIndex, RetryDelays.Length - 1)], ct);
                retryIndex = Math.Min(retryIndex + 1, RetryDelays.Length - 1);
            }
        }
    }

    private Task ApplyConfigSnapshotAsync(ConfigSnapshot snapshot)
    {
        return _syncLock.WaitAsync().ContinueWith(async _ =>
        {
            try
            {
                _configProvider.ApplyConfig(snapshot);

                if (snapshot.Certificates.Count > 0)
                {
                    _configProvider.UpdateCertificates(snapshot.Certificates);
                    _certSelector.ReloadFromProvider();
                }

                _initialConfigReceived = true;
                _logger.LogInformation(
                    "Config updated to version {Version} ({UpdateType}, {RouteCount} routes, {ClusterCount} clusters)",
                    snapshot.Version,
                    snapshot.UpdateType,
                    snapshot.Routes.Count,
                    snapshot.Clusters.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply config snapshot version {Version}", snapshot.Version);
            }
            finally
            {
                _syncLock.Release();
            }
        }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _call?.Dispose();
            _syncLock.Dispose();
            _disposed = true;
        }
    }
}
