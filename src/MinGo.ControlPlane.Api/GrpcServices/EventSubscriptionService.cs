using System.Collections.Concurrent;
using Grpc.Core;
using MinGo.ControlPlane.Api.Services;
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
    private readonly InstanceConfigQueryService _configQueryService;
    private readonly ILogger<EventSubscriptionService> _logger;

    // 跟踪已连接的数据面及其响应流
    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new();

    public EventSubscriptionService(
        IServiceScopeFactory scopeFactory,
        InstanceConfigQueryService configQueryService,
        ILogger<EventSubscriptionService> logger)
    {
        _scopeFactory = scopeFactory;
        _configQueryService = configQueryService;
        _logger = logger;
    }

    public override async Task SubscribeEvents(
        IAsyncStreamReader<EventMessage> requestStream,
        IServerStreamWriter<EventMessage> responseStream,
        ServerCallContext context)
    {
        // 先读取第一个事件来确定 dataPlaneId
        if (!await requestStream.MoveNext(context.CancellationToken))
            return;

        var firstMsg = requestStream.Current;
        var dataPlaneId = firstMsg.Source;

        // 注册连接
        var entry = new ConnectionEntry(dataPlaneId, responseStream);
        _connections[dataPlaneId] = entry;
        _logger.LogInformation("Data plane {DataPlaneId} registered for event subscription", dataPlaneId);

        try
        {
            // 处理第一个事件
            await ProcessEventAsync(firstMsg);

            // 持续接收后续事件
            await foreach (var eventMsg in requestStream.ReadAllAsync(context.CancellationToken))
            {
                await ProcessEventAsync(eventMsg);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event subscription stream ended for {DataPlaneId}", dataPlaneId);
        }
        finally
        {
            _connections.TryRemove(dataPlaneId, out _);
            _logger.LogInformation("Data plane {DataPlaneId} unregistered from event subscription", dataPlaneId);
        }
    }

    private async Task ProcessEventAsync(EventMessage eventMsg)
    {
        _logger.LogInformation("Received event {EventId} type {EventType} from {Source}",
            eventMsg.EventId, eventMsg.Type, eventMsg.Source);

        // 持久化事件到数据库
        await PersistEventAsync(eventMsg);

        // 处理特定事件
        switch (eventMsg.Type)
        {
            case EventType.ConfigReport:
                HandleConfigReport(eventMsg);
                break;

            case EventType.ErrorAlert:
                _logger.LogWarning("Error alert from {Source}: {Data}", eventMsg.Source, eventMsg.DataJson);
                break;

            case EventType.HealthStatusChange:
                break;

            case EventType.InstanceStatusChange:
                _logger.LogInformation("Instance status change from {Source}: {Data}", eventMsg.Source, eventMsg.DataJson);
                break;
        }
    }

    private void HandleConfigReport(EventMessage eventMsg)
    {
        _configQueryService.HandleConfigReport(eventMsg.EventId, eventMsg.DataJson);
    }

    /// <summary>
    /// 向指定数据面发送事件
    /// </summary>
    public async Task<bool> TrySendEventAsync(string dataPlaneId, EventMessage eventMsg)
    {
        if (_connections.TryGetValue(dataPlaneId, out var entry))
        {
            try
            {
                await entry.ResponseStream.WriteAsync(eventMsg);
                _logger.LogDebug("Event {EventId} type {EventType} sent to {DataPlaneId}",
                    eventMsg.EventId, eventMsg.Type, dataPlaneId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send event to {DataPlaneId}, removing connection", dataPlaneId);
                _connections.TryRemove(dataPlaneId, out _);
            }
        }
        else
        {
            _logger.LogDebug("Data plane {DataPlaneId} not connected for event subscription", dataPlaneId);
        }
        return false;
    }

    public bool IsConnected(string dataPlaneId) => _connections.ContainsKey(dataPlaneId);

    private async Task PersistEventAsync(EventMessage eventMsg)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var eventService = scope.ServiceProvider.GetRequiredService<IGatewayEventService>();

            var gatewayEvent = new GatewayEvent
            {
                EventId = eventMsg.EventId,
                EventType = (GatewayEventType)((int)eventMsg.Type - 1),
                EventTime = DateTimeOffset.FromUnixTimeMilliseconds(eventMsg.TimestampUnixMs),
                Source = eventMsg.Source,
                EventDataJson = eventMsg.DataJson,
                Priority = eventMsg.Priority,
                IsProcessed = false
            };

            var notificationService = scope.ServiceProvider.GetRequiredService<IMessageNotificationService>();
            await notificationService.PublishEventAsync(gatewayEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist event {EventId}", eventMsg.EventId);
        }
    }

    private record ConnectionEntry(string DataPlaneId, IServerStreamWriter<EventMessage> ResponseStream);
}
