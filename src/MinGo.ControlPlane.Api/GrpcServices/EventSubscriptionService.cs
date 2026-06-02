using Grpc.Core;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.DataPlane.Grpc;

namespace MinGo.ControlPlane.Api.GrpcServices;

/// <summary>
/// 事件订阅 gRPC 服务 - 控制面与数据面双向事件通道
/// </summary>
public class EventSubscriptionService : EventSubscription.EventSubscriptionBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventSubscriptionService> _logger;

    public EventSubscriptionService(
        IServiceScopeFactory scopeFactory,
        ILogger<EventSubscriptionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override async Task SubscribeEvents(
        IAsyncStreamReader<EventMessage> requestStream,
        IServerStreamWriter<EventMessage> responseStream,
        ServerCallContext context)
    {
        try
        {
            await foreach (var eventMsg in requestStream.ReadAllAsync(context.CancellationToken))
            {
                _logger.LogInformation("Received event {EventId} type {EventType} from {Source}",
                    eventMsg.EventId, eventMsg.Type, eventMsg.Source);

                // 持久化事件到数据库
                await PersistEventAsync(eventMsg);

                // 处理特定事件
                await HandleEventAsync(eventMsg, responseStream, context.CancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event subscription stream ended");
        }
    }

    private async Task PersistEventAsync(EventMessage eventMsg)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var eventService = scope.ServiceProvider.GetRequiredService<IGatewayEventService>();

            var gatewayEvent = new GatewayEvent
            {
                EventId = eventMsg.EventId,
                EventType = (GatewayEventType)((int)eventMsg.Type - 1), // protobuf enum 从 1 开始
                EventTime = DateTimeOffset.FromUnixTimeMilliseconds(eventMsg.TimestampUnixMs),
                Source = eventMsg.Source,
                EventDataJson = eventMsg.DataJson,
                Priority = eventMsg.Priority,
                IsProcessed = false
            };

            // 通过消息通知服务发布事件
            var notificationService = scope.ServiceProvider.GetRequiredService<IMessageNotificationService>();
            await notificationService.PublishEventAsync(gatewayEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist event {EventId}", eventMsg.EventId);
        }
    }

    private async Task HandleEventAsync(
        EventMessage eventMsg,
        IServerStreamWriter<EventMessage> responseStream,
        CancellationToken ct)
    {
        // 根据事件类型处理
        switch (eventMsg.Type)
        {
            case EventType.ErrorAlert:
                _logger.LogWarning("Error alert from {Source}: {Data}", eventMsg.Source, eventMsg.DataJson);
                break;

            case EventType.HealthStatusChange:
                // 可以广播给其他数据面
                break;

            case EventType.InstanceStatusChange:
                _logger.LogInformation("Instance status change: {Data}", eventMsg.DataJson);
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 向指定数据面发送事件
    /// </summary>
    public async Task SendEventAsync(EventMessage eventMsg)
    {
        // 当前实现通过 HeartbeatCollect 的响应下发指令
        // 或通过 ConfigReplication 的配置更新推送
        await Task.CompletedTask;
    }
}
