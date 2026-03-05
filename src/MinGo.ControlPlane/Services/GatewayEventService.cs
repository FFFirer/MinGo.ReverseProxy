using MinGo.Shared.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace MinGo.ControlPlane.Services;

/// <summary>
/// Gateway事件下发服务实现
/// </summary>
public class GatewayEventService : IGatewayEventService
{
    private readonly IGatewayInstanceService _gatewayInstanceService;
    private readonly IGatewayEventSender _eventSender;
    private readonly ConcurrentDictionary<string, GatewayEventSubscriptionRequest> _subscriptions;
    private readonly ConcurrentQueue<GatewayEvent> _eventHistory;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="gatewayInstanceService">网关实例服务</param>
    /// <param name="eventSender">事件发送器</param>
    public GatewayEventService(IGatewayInstanceService gatewayInstanceService, IGatewayEventSender eventSender)
    {
        _gatewayInstanceService = gatewayInstanceService;
        _eventSender = eventSender;
        _subscriptions = new ConcurrentDictionary<string, GatewayEventSubscriptionRequest>();
        _eventHistory = new ConcurrentQueue<GatewayEvent>();
    }

    /// <summary>
    /// 发送事件到所有实例
    /// </summary>
    /// <param name="eventType">事件类型</param>
    /// <param name="eventData">事件数据</param>
    /// <param name="priority">事件优先级</param>
    /// <returns>事件响应列表</returns>
    public async Task<List<GatewayEventResponse>> SendEventToAllInstancesAsync(GatewayEventType eventType, object eventData, int priority = 0)
    {
        var instancesResponse = await _gatewayInstanceService.GetInstancesAsync();
        var onlineInstances = instancesResponse.Instances.Where(i => i.Status == GatewayInstanceStatus.Online);
        var instanceIds = onlineInstances.Select(i => i.InstanceId).ToList();

        return await SendEventToInstancesAsync(instanceIds, eventType, eventData, priority);
    }

    /// <summary>
    /// 发送事件到指定实例
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="eventType">事件类型</param>
    /// <param name="eventData">事件数据</param>
    /// <param name="priority">事件优先级</param>
    /// <returns>事件响应</returns>
    public async Task<GatewayEventResponse?> SendEventToInstanceAsync(string instanceId, GatewayEventType eventType, object eventData, int priority = 0)
    {
        var instance = await _gatewayInstanceService.GetInstanceAsync(instanceId);
        if (instance == null || instance.Status != GatewayInstanceStatus.Online)
        {
            return null;
        }

        var eventDataJson = JsonSerializer.Serialize(eventData);
        var gatewayEvent = new GatewayEvent
        {
            EventType = eventType,
            EventData = eventDataJson,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TargetInstanceId = instanceId,
            Priority = priority
        };

        // 保存事件到历史记录
        _eventHistory.Enqueue(gatewayEvent);
        // 限制历史记录数量
        while (_eventHistory.Count > 1000)
        {
            _eventHistory.TryDequeue(out _);
        }

        // 使用事件发送器发送事件
        var response = await _eventSender.SendEventAsync(instance, gatewayEvent);
        return response;
    }

    /// <summary>
    /// 发送事件到多个实例
    /// </summary>
    /// <param name="instanceIds">实例ID列表</param>
    /// <param name="eventType">事件类型</param>
    /// <param name="eventData">事件数据</param>
    /// <param name="priority">事件优先级</param>
    /// <returns>事件响应列表</returns>
    public async Task<List<GatewayEventResponse>> SendEventToInstancesAsync(IEnumerable<string> instanceIds, GatewayEventType eventType, object eventData, int priority = 0)
    {
        var responses = new List<GatewayEventResponse>();

        foreach (var instanceId in instanceIds)
        {
            var response = await SendEventToInstanceAsync(instanceId, eventType, eventData, priority);
            if (response != null)
            {
                responses.Add(response);
            }
        }

        return responses;
    }

    /// <summary>
    /// 订阅事件
    /// </summary>
    /// <param name="request">订阅请求</param>
    /// <returns>是否成功</returns>
    public Task<bool> SubscribeToEventsAsync(GatewayEventSubscriptionRequest request)
    {
        _subscriptions[request.InstanceId] = request;
        return Task.FromResult(true);
    }

    /// <summary>
    /// 取消订阅事件
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <returns>是否成功</returns>
    public Task<bool> UnsubscribeFromEventsAsync(string instanceId)
    {
        return Task.FromResult(_subscriptions.TryRemove(instanceId, out _));
    }

    /// <summary>
    /// 获取事件历史
    /// </summary>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="eventType">事件类型（可选）</param>
    /// <returns>事件列表</returns>
    public Task<List<GatewayEvent>> GetEventHistoryAsync(DateTimeOffset startTime, DateTimeOffset endTime, GatewayEventType? eventType = null)
    {
        var events = _eventHistory.Where(e => 
            e.CreatedAt >= startTime && 
            e.CreatedAt <= endTime &&
            (!eventType.HasValue || e.EventType == eventType.Value)
        ).ToList();

        return Task.FromResult(events);
    }

    /// <summary>
    /// 清理过期事件
    /// </summary>
    /// <returns>清理的事件数量</returns>
    public Task<int> CleanupExpiredEventsAsync()
    {
        int count = 0;
        var now = DateTimeOffset.UtcNow;

        while (_eventHistory.TryPeek(out var ev) && ev.ExpiresAt < now)
        {
            _eventHistory.TryDequeue(out _);
            count++;
        }

        // 清理过期的订阅
        var expiredSubscriptions = _subscriptions.Where(s => s.Value.ExpiresAt < now).Select(s => s.Key).ToList();
        foreach (var instanceId in expiredSubscriptions)
        {
            _subscriptions.TryRemove(instanceId, out _);
        }

        return Task.FromResult(count);
    }
}

/// <summary>
/// HTTP事件发送器实现
/// </summary>
public class HttpGatewayEventSender : IGatewayEventSender
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="httpClientFactory">HTTP客户端工厂</param>
    public HttpGatewayEventSender(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// 发送事件到实例
    /// </summary>
    /// <param name="instance">实例信息</param>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>事件响应</returns>
    public async Task<GatewayEventResponse> SendEventAsync(GatewayInstance instance, GatewayEvent gatewayEvent)
    {
        var response = new GatewayEventResponse
        {
            EventId = gatewayEvent.EventId,
            InstanceId = instance.InstanceId,
            Success = false
        };

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            // 构建实例的事件接收URL
            var eventUrl = $"http://{instance.IpAddress}:{instance.Port}/api/events";
            var content = new StringContent(
                JsonSerializer.Serialize(gatewayEvent),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var httpResponse = await httpClient.PostAsync(eventUrl, content);
            httpResponse.EnsureSuccessStatusCode();

            var responseContent = await httpResponse.Content.ReadAsStringAsync();
            var eventResponse = JsonSerializer.Deserialize<GatewayEventResponse>(responseContent);

            if (eventResponse != null)
            {
                response = eventResponse;
            }
            else
            {
                response.Success = true;
            }
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
        }

        return response;
    }
}
