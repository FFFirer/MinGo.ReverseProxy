using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinGo.DataPlane.Grpc;
using MinGo.DataPlane.Kestrel;

namespace MinGo.DataPlane.ConfigSync;

/// <summary>
/// 配置同步服务 - 通过 gRPC 双向流从控制面同步配置
/// </summary>
public class ConfigSyncService : BackgroundService
{
    private readonly ConfigReplication.ConfigReplicationClient _client;
    private readonly DataPlaneConfigProvider _configProvider;
    private readonly DataPlaneCertificateSelector _certSelector;
    private readonly GatewayIdentity _identity;
    private readonly ILogger<ConfigSyncService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
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
        GatewayIdentity identity,
        ILogger<ConfigSyncService> logger)
    {
        _client = client;
        _configProvider = configProvider;
        _certSelector = certSelector;
        _identity = identity;
        _logger = logger;
    }

    public string DataPlaneId => _identity.Id;

    public bool InitialConfigReceived => _initialConfigReceived;

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryIndex = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting to control plane gRPC ConfigReplication...");

                using var call = _client.ReplicateConfig(cancellationToken: stoppingToken);

                await call.RequestStream.WriteAsync(new ConfigSubscription
                {
                    DataPlaneId = _identity.Id,
                    CurrentConfigVersion = _configProvider.CurrentVersion,
                    SubscribeCertificates = true
                });

                _logger.LogInformation("Connected to control plane, waiting for config...");
                retryIndex = 0;

                await foreach (var snapshot in call.ResponseStream.ReadAllAsync(stoppingToken))
                {
                    await ApplyConfigSnapshotAsync(snapshot);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Config sync connection lost, retrying in {Delay}...",
                    RetryDelays[Math.Min(retryIndex, RetryDelays.Length - 1)]);

                await Task.Delay(RetryDelays[Math.Min(retryIndex, RetryDelays.Length - 1)], stoppingToken);
                retryIndex = Math.Min(retryIndex + 1, RetryDelays.Length - 1);
            }
        }
    }

    private async Task ApplyConfigSnapshotAsync(ConfigSnapshot snapshot)
    {
        await _syncLock.WaitAsync();
        try
        {
            _configProvider.ApplyConfig(snapshot);

            if (snapshot.Certificates.Count > 0)
            {
                _configProvider.UpdateCertificates(snapshot.Certificates);
                _certSelector.ReloadFromProvider();
            }

            _initialConfigReceived = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply config snapshot version {Version}", snapshot.Version);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _syncLock.Dispose();
    }
}
