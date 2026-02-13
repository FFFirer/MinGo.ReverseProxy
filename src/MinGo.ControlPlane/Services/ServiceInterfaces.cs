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
    Task<MinGo.Shared.Models.RouteConfig> GetRouteAsync(string id);
    Task<MinGo.Shared.Models.RouteConfig> CreateRouteAsync(MinGo.Shared.Models.RouteConfig route);
    Task<MinGo.Shared.Models.RouteConfig> UpdateRouteAsync(string id, MinGo.Shared.Models.RouteConfig route);
    Task DeleteRouteAsync(string id);

    Task<IEnumerable<ClusterConfig>> GetClustersAsync();
    Task<ClusterConfig> GetClusterAsync(string id);
    Task<ClusterConfig> CreateClusterAsync(ClusterConfig cluster);
    Task<ClusterConfig> UpdateClusterAsync(string id, ClusterConfig cluster);
    Task DeleteClusterAsync(string id);
}
