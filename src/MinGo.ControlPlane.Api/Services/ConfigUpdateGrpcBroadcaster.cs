using MinGo.ControlPlane.Api.GrpcServices;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;

namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// 配置变更 gRPC 广播服务 - 监听配置更新事件并推送到数据面
/// </summary>
public class ConfigUpdateGrpcBroadcaster : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConfigUpdateGrpcBroadcaster> _logger;
    private string _subscriptionId = string.Empty;

    public ConfigUpdateGrpcBroadcaster(
        IServiceProvider serviceProvider,
        ILogger<ConfigUpdateGrpcBroadcaster> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Config update gRPC broadcaster starting...");

        using var scope = _serviceProvider.CreateScope();
        var notificationService = scope.ServiceProvider.GetRequiredService<IMessageNotificationService>();

        _subscriptionId = await notificationService.SubscribeAsync(
            "ConfigUpdateGrpcBroadcaster",
            new[] { GatewayEventType.ConfigUpdate },
            async (gatewayEvent) => await HandleConfigUpdateAsync(gatewayEvent));

        _logger.LogInformation("Config update gRPC broadcaster started, subscription: {Id}", _subscriptionId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var notificationService = scope.ServiceProvider.GetRequiredService<IMessageNotificationService>();

        if (!string.IsNullOrEmpty(_subscriptionId))
            await notificationService.UnsubscribeAsync(_subscriptionId);

        _logger.LogInformation("Config update gRPC broadcaster stopped");
    }

    private async Task HandleConfigUpdateAsync(GatewayEvent gatewayEvent)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var configReplication = scope.ServiceProvider.GetRequiredService<ConfigReplicationService>();
            await configReplication.BroadcastConfigUpdateAsync();
            _logger.LogInformation("gRPC config broadcast completed for event {EventId}", gatewayEvent.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast config update via gRPC");
        }
    }
}
