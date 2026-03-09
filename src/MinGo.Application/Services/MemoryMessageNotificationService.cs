using Microsoft.Extensions.Logging;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;

namespace MinGo.Application.Services;

/// <summary>
/// 基于内存的消息通知服务
/// </summary>
public class MemoryMessageNotificationService : IMessageNotificationService
{
    private readonly ILogger<MemoryMessageNotificationService> _logger;
    private readonly List<GatewayEvent> _eventQueue = new();
    private readonly Dictionary<string, Subscription> _subscriptions = new();
    private readonly object _lock = new();

    /// <summary>
    /// 订阅信息
    /// </summary>
    private class Subscription
    {
        public required string SubscriberId { get; set; }
        public List<GatewayEventType> EventTypes { get; set; } = [];
        public required Func<GatewayEvent, Task> Callback { get; set; } 
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public MemoryMessageNotificationService(ILogger<MemoryMessageNotificationService> logger)
    {
        _logger = logger;
        // 启动后台任务处理事件
        _ = Task.Run(async () => await ProcessEventsAsync());
    }

    /// <summary>
    /// 发布事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>是否成功</returns>
    public Task<bool> PublishEventAsync(GatewayEvent gatewayEvent)
    {
        try
        {
            lock (_lock)
            {
                _eventQueue.Add(gatewayEvent);
                _logger.LogDebug("Event published: {EventType} with ID {EventId}", gatewayEvent.EventType, gatewayEvent.EventId);
            }
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 订阅事件
    /// </summary>
    /// <param name="subscriberId">订阅者ID</param>
    /// <param name="eventTypes">订阅的事件类型</param>
    /// <param name="callback">回调函数</param>
    /// <returns>订阅ID</returns>
    public Task<string> SubscribeAsync(string subscriberId, IEnumerable<GatewayEventType> eventTypes, Func<GatewayEvent, Task> callback)
    {
        var subscriptionId = Guid.NewGuid().ToString();
        var subscription = new Subscription
        {
            SubscriberId = subscriberId,
            EventTypes = [.. eventTypes],
            Callback = callback,
            CreatedAt = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            _subscriptions[subscriptionId] = subscription;
            _logger.LogInformation("Subscriber {SubscriberId} subscribed to events: {EventTypes}", 
                subscriberId, string.Join(", ", eventTypes.Select(t => t.ToString())));
        }

        return Task.FromResult(subscriptionId);
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    /// <param name="subscriptionId">订阅ID</param>
    /// <returns>是否成功</returns>
    public Task<bool> UnsubscribeAsync(string subscriptionId)
    {
        lock (_lock)
        {
            if (_subscriptions.TryGetValue(subscriptionId, out var subscription))
            {
                _subscriptions.Remove(subscriptionId);
                _logger.LogInformation("Subscriber {SubscriberId} unsubscribed from events", subscription.SubscriberId);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 获取事件队列大小
    /// </summary>
    /// <returns>队列大小</returns>
    public int GetQueueSize()
    {
        lock (_lock)
        {
            return _eventQueue.Count;
        }
    }

    /// <summary>
    /// 清理过期事件
    /// </summary>
    /// <param name="expirationTime">过期时间</param>
    /// <returns>清理的事件数量</returns>
    public Task<int> CleanupExpiredEventsAsync(TimeSpan expirationTime)
    {
        var cutoffTime = DateTimeOffset.UtcNow - expirationTime;
        var removedCount = 0;

        lock (_lock)
        {
            var expiredEvents = _eventQueue.Where(e => e.EventTime < cutoffTime).ToList();
            foreach (var expiredEvent in expiredEvents)
            {
                _eventQueue.Remove(expiredEvent);
                removedCount++;
            }
        }

        if (removedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired events", removedCount);
        }

        return Task.FromResult(removedCount);
    }

    /// <summary>
    /// 处理事件的后台任务
    /// </summary>
    /// <returns>任务</returns>
    private async Task ProcessEventsAsync()
    {
        while (true)
        {
            try
            {
                GatewayEvent? @event = null;
                List<Subscription> relevantSubscriptions = new();

                lock (_lock)
                {
                    if (_eventQueue.Count > 0)
                    {
                        @event = _eventQueue[0];
                        _eventQueue.RemoveAt(0);

                        // 找到对该事件类型感兴趣的订阅者
                        relevantSubscriptions = _subscriptions.Values
                            .Where(s => s.EventTypes.Contains(@event.EventType))
                            .ToList();
                    }
                }

                if (@event != null && relevantSubscriptions.Count > 0)
                {
                    _logger.LogDebug("Processing event {EventId} of type {EventType} for {Count} subscribers", 
                        @event.EventId, @event.EventType, relevantSubscriptions.Count);

                    // 异步通知所有相关订阅者
                    var notificationTasks = relevantSubscriptions.Select(async subscription =>
                    {
                        try
                        {
                            await subscription.Callback(@event!);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error notifying subscriber {SubscriberId} for event {EventId}", 
                                subscription.SubscriberId, @event!.EventId);
                        }
                    });

                    await Task.WhenAll(notificationTasks);

                    // 标记事件为已处理
                    @event.IsProcessed = true;
                    @event.ProcessedAt = DateTimeOffset.UtcNow;
                }
                else
                {
                    // 队列为空，短暂休眠
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing events");
                // 避免异常导致整个处理循环崩溃
                await Task.Delay(1000);
            }
        }
    }
}
