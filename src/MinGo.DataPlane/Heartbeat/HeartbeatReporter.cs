using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinGo.DataPlane.Grpc;

namespace MinGo.DataPlane.Heartbeat;

/// <summary>
/// 心跳上报服务 - 通过 gRPC 双向流定期上报系统指标
/// 请求内容由 <see cref="HeartbeatRequestBuilder"/> 构造；本类只管连接、节律与重连。
/// </summary>
public class HeartbeatReporter : BackgroundService
{
    private readonly HeartbeatCollect.HeartbeatCollectClient _client;
    private readonly HeartbeatRequestBuilder _builder;
    private readonly ILogger<HeartbeatReporter> _logger;

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    public HeartbeatReporter(
        HeartbeatCollect.HeartbeatCollectClient client,
        HeartbeatRequestBuilder builder,
        ILogger<HeartbeatReporter> logger)
    {
        _client = client;
        _builder = builder;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var call = _client.ReportHeartbeat(cancellationToken: stoppingToken);
                _logger.LogInformation("Heartbeat connection established");

                retryDelay = TimeSpan.FromSeconds(1);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(HeartbeatInterval, stoppingToken);

                    var request = _builder.Build();
                    await call.RequestStream.WriteAsync(request, stoppingToken);

                    // 只有写成功了才算送达。写失败时游标不动，下一周期重取同一页 —— 
                    // 至多重复一个周期，而不是把没送出去的数据当成已上报。
                    _builder.Commit();

                    if (await call.ResponseStream.MoveNext(stoppingToken))
                    {
                        var response = call.ResponseStream.Current;
                        if (response.Commands.Count > 0)
                        {
                            await HandleCommandsAsync(response.Commands, stoppingToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Heartbeat connection lost, retrying in {Delay}...", retryDelay);
                await Task.Delay(retryDelay, stoppingToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
            }
        }
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
                    break;
            }
        }
    }

}
