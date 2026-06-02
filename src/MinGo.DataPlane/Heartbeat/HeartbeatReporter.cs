using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinGo.Core.Services;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Grpc;
using CoreMetricPoint = MinGo.Core.Services.MetricPoint;

namespace MinGo.DataPlane.Heartbeat;

/// <summary>
/// 心跳上报服务 - 通过 gRPC 双向流定期上报系统指标
/// </summary>
public class HeartbeatReporter : IHostedService, IDisposable
{
    private readonly HeartbeatCollect.HeartbeatCollectClient _client;
    private readonly TelemetryStore _telemetryStore;
    private readonly DataPlaneConfigProvider _configProvider;
    private readonly ConfigSyncService _configSync;
    private readonly ILogger<HeartbeatReporter> _logger;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    public HeartbeatReporter(
        HeartbeatCollect.HeartbeatCollectClient client,
        TelemetryStore telemetryStore,
        DataPlaneConfigProvider configProvider,
        ConfigSyncService configSync,
        ILogger<HeartbeatReporter> logger)
    {
        _client = client;
        _telemetryStore = telemetryStore;
        _configProvider = configProvider;
        _configSync = configSync;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RunHeartbeatLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(1);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var call = _client.ReportHeartbeat(cancellationToken: ct);
                _logger.LogInformation("Heartbeat connection established");

                retryDelay = TimeSpan.FromSeconds(1);

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(HeartbeatInterval, ct);

                    var request = BuildHeartbeatRequest();
                    await call.RequestStream.WriteAsync(request, ct);

                    // 读取响应（含可能的控制指令）
                    if (await call.ResponseStream.MoveNext(ct))
                    {
                        var response = call.ResponseStream.Current;
                        if (response.Commands.Count > 0)
                        {
                            await HandleCommandsAsync(response.Commands, ct);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Heartbeat connection lost, retrying in {Delay}...", retryDelay);
                await Task.Delay(retryDelay, ct);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
            }
        }
    }

    private HeartbeatRequest BuildHeartbeatRequest()
    {
        var totalRequests = _telemetryStore.Metrics.TryGetValue("gateway.requests.total", out var total)
            ? (long)total.Sum(p => p.Value) : 0;
        var errorRequests = _telemetryStore.Metrics.TryGetValue("gateway.requests.errors", out var errors)
            ? (long)errors.Sum(p => p.Value) : 0;

        var request = new HeartbeatRequest
        {
            DataPlaneId = _configSync.DataPlaneId,
            CpuUsage = 0,
            MemoryUsage = 0,
            TotalRequests = totalRequests,
            ErrorRequests = errorRequests,
            IsHealthy = true,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // 添加遥测指标点
        foreach (var (name, points) in _telemetryStore.Metrics)
        {
            foreach (var point in points.TakeLast(10))
            {
                request.Metrics.Add(new Grpc.MetricPoint
                {
                    Name = name,
                    Value = point.Value,
                    TimestampUnixMs = new DateTimeOffset(point.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds()
                });
            }
        }

        return request;
    }

    private async Task HandleCommandsAsync(IEnumerable<ControlCommand> commands, CancellationToken ct)
    {
        foreach (var cmd in commands)
        {
            _logger.LogInformation("Received control command: {CommandType}", cmd.Type);

            switch (cmd.Type)
            {
                case CommandType.CmdReloadConfig:
                    // ConfigSyncService 会自动处理重连获取新配置
                    _logger.LogInformation("Config reload requested by control plane");
                    break;

                case CommandType.CmdReloadCerts:
                    _logger.LogInformation("Certificate reload requested by control plane");
                    break;

                case CommandType.CmdShutdown:
                    _logger.LogWarning("Shutdown requested by control plane");
                    _cts?.Cancel();
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _disposed = true;
        }
    }
}
