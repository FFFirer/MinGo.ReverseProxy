using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Services;

public interface IConfigService
{
    Task<GatewayConfig> GetConfigAsync();
    Task<GatewayConfig> GetConfigByVersionAsync(string version);
    Task<GatewayConfig> CreateConfigAsync(GatewayConfig config);
    Task<GatewayConfig> UpdateConfigAsync(string id, GatewayConfig config);
    Task DeleteConfigAsync(string id);
    Task NotifyConfigChangeAsync(GatewayConfig config);
}

public interface IMonitoringService
{
    Task<MetricsSummary> GetMetricsSummaryAsync();
    Task<IEnumerable<RequestMetrics>> GetRequestMetricsAsync(DateTimeOffset start, DateTimeOffset end);
    Task<IEnumerable<ServiceMetrics>> GetServiceMetricsAsync();
    Task<IEnumerable<ErrorMetrics>> GetErrorMetricsAsync(DateTimeOffset start, DateTimeOffset end);
    Task<SystemMetrics> GetSystemMetricsAsync();
}

public interface ILogService
{
    Task<IEnumerable<AccessLog>> GetAccessLogsAsync(LogQuery query);
    Task<IEnumerable<ErrorLog>> GetErrorLogsAsync(LogQuery query);
    Task<LogStatistics> GetLogStatisticsAsync(DateTimeOffset start, DateTimeOffset end);
    Task ExportLogsAsync(LogQuery query, Stream stream);
}

public interface IApiManagementService
{
    Task<IEnumerable<MinGo.Shared.Models.RouteConfig>> GetRoutesAsync();
    Task<MinGo.Shared.Models.RouteConfig?> GetRouteAsync(string id);
    Task<MinGo.Shared.Models.RouteConfig> CreateRouteAsync(MinGo.Shared.Models.RouteConfig route);
    Task<MinGo.Shared.Models.RouteConfig?> UpdateRouteAsync(string id, MinGo.Shared.Models.RouteConfig route);
    Task DeleteRouteAsync(string id);

    Task<IEnumerable<ClusterConfig>> GetClustersAsync();
    Task<ClusterConfig?> GetClusterAsync(string id);
    Task<ClusterConfig> CreateClusterAsync(ClusterConfig cluster);
    Task<ClusterConfig?> UpdateClusterAsync(string id, ClusterConfig cluster);
    Task DeleteClusterAsync(string id);

    Task<ClusterConfig?> AddDestinationAsync(string clusterId, string destinationId, DestinationConfig destination);
    Task<ClusterConfig?> UpdateDestinationAsync(string clusterId, string destinationId, DestinationConfig destination);
    Task<ClusterConfig?> RemoveDestinationAsync(string clusterId, string destinationId);

    Task<IEnumerable<CertificateConfig>> GetCertificatesAsync();
    Task<CertificateConfig?> GetCertificateAsync(string id);
    Task<CertificateConfig> CreateCertificateAsync(CertificateConfig certificate);
    Task<CertificateConfig?> UpdateCertificateAsync(string id, CertificateConfig certificate);
    Task DeleteCertificateAsync(string id);
}

/// <summary>
/// Gateway实例管理服务接口
/// </summary>
public interface IGatewayInstanceService
{
    /// <summary>
    /// 注册Gateway实例
    /// </summary>
    /// <param name="request">注册请求</param>
    /// <returns>注册结果</returns>
    Task<GatewayInstance> RegisterInstanceAsync(GatewayInstanceRegisterRequest request);

    /// <summary>
    /// 更新实例心跳
    /// </summary>
    /// <param name="request">心跳请求</param>
    /// <returns>是否成功</returns>
    Task<bool> UpdateHeartbeatAsync(GatewayInstanceHeartbeatRequest request);

    /// <summary>
    /// 获取所有在线实例
    /// </summary>
    /// <returns>实例列表响应</returns>
    Task<GatewayInstanceListResponse> GetInstancesAsync();

    /// <summary>
    /// 获取指定实例
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <returns>实例信息</returns>
    Task<GatewayInstance?> GetInstanceAsync(string instanceId);

    /// <summary>
    /// 移除实例
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <returns>是否成功</returns>
    Task<bool> RemoveInstanceAsync(string instanceId);

    /// <summary>
    /// 检查并更新超时实例状态
    /// </summary>
    /// <returns>超时实例数量</returns>
    Task<int> CheckAndUpdateTimeoutInstancesAsync();

    /// <summary>
    /// 清理长时间心跳超时的实例
    /// </summary>
    /// <returns>清理的实例数量</returns>
    Task<int> CleanupLongTimeTimeoutInstancesAsync();
}

/// <summary>
/// Gateway事件发送器接口
/// </summary>
public interface IGatewayEventSender
{
    /// <summary>
    /// 发送事件到实例
    /// </summary>
    /// <param name="instance">实例信息</param>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>事件响应</returns>
    Task<GatewayEventResponse> SendEventAsync(GatewayInstance instance, GatewayEvent gatewayEvent);
}

/// <summary>
/// Gateway事件下发服务接口
/// </summary>
public interface IGatewayEventService
{
    /// <summary>
    /// 发送事件到所有实例
    /// </summary>
    /// <param name="eventType">事件类型</param>
    /// <param name="eventData">事件数据</param>
    /// <param name="priority">事件优先级</param>
    /// <returns>事件响应列表</returns>
    Task<List<GatewayEventResponse>> SendEventToAllInstancesAsync(GatewayEventType eventType, object eventData, int priority = 0);

    /// <summary>
    /// 发送事件到指定实例
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="eventType">事件类型</param>
    /// <param name="eventData">事件数据</param>
    /// <param name="priority">事件优先级</param>
    /// <returns>事件响应</returns>
    Task<GatewayEventResponse?> SendEventToInstanceAsync(string instanceId, GatewayEventType eventType, object eventData, int priority = 0);

    /// <summary>
    /// 发送事件到多个实例
    /// </summary>
    /// <param name="instanceIds">实例ID列表</param>
    /// <param name="eventType">事件类型</param>
    /// <param name="eventData">事件数据</param>
    /// <param name="priority">事件优先级</param>
    /// <returns>事件响应列表</returns>
    Task<List<GatewayEventResponse>> SendEventToInstancesAsync(IEnumerable<string> instanceIds, GatewayEventType eventType, object eventData, int priority = 0);

    /// <summary>
    /// 订阅事件
    /// </summary>
    /// <param name="request">订阅请求</param>
    /// <returns>是否成功</returns>
    Task<bool> SubscribeToEventsAsync(GatewayEventSubscriptionRequest request);

    /// <summary>
    /// 取消订阅事件
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <returns>是否成功</returns>
    Task<bool> UnsubscribeFromEventsAsync(string instanceId);

    /// <summary>
    /// 获取事件历史
    /// </summary>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="eventType">事件类型（可选）</param>
    /// <returns>事件列表</returns>
    Task<List<GatewayEvent>> GetEventHistoryAsync(DateTimeOffset startTime, DateTimeOffset endTime, GatewayEventType? eventType = null);

    /// <summary>
    /// 清理过期事件
    /// </summary>
    /// <returns>清理的事件数量</returns>
    Task<int> CleanupExpiredEventsAsync();
}
