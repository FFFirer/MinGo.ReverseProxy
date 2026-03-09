using MinGo.Core.Entities;

namespace MinGo.Core.Interfaces;

/// <summary>
/// 消息通知服务接口
/// </summary>
public interface IMessageNotificationService
{
    /// <summary>
    /// 发布事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>是否成功</returns>
    Task<bool> PublishEventAsync(GatewayEvent gatewayEvent);

    /// <summary>
    /// 订阅事件
    /// </summary>
    /// <param name="subscriberId">订阅者ID</param>
    /// <param name="eventTypes">订阅的事件类型</param>
    /// <param name="callback">回调函数</param>
    /// <returns>订阅ID</returns>
    Task<string> SubscribeAsync(string subscriberId, IEnumerable<GatewayEventType> eventTypes, Func<GatewayEvent, Task> callback);

    /// <summary>
    /// 取消订阅
    /// </summary>
    /// <param name="subscriptionId">订阅ID</param>
    /// <returns>是否成功</returns>
    Task<bool> UnsubscribeAsync(string subscriptionId);

    /// <summary>
    /// 获取事件队列大小
    /// </summary>
    /// <returns>队列大小</returns>
    int GetQueueSize();

    /// <summary>
    /// 清理过期事件
    /// </summary>
    /// <param name="expirationTime">过期时间</param>
    /// <returns>清理的事件数量</returns>
    Task<int> CleanupExpiredEventsAsync(TimeSpan expirationTime);
}
