using Microsoft.Extensions.Logging;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.Application.Services;

public class ApiManagementService : IApiManagementService
{
    private readonly ILogger<ApiManagementService> _logger;
    private readonly IApiDbService _apiDbService;
    private readonly IGatewayEventService _gatewayEventService;

    public ApiManagementService(ILogger<ApiManagementService> logger, IApiDbService apiDbService, IGatewayEventService gatewayEventService)
    {
        _logger = logger;
        _apiDbService = apiDbService;
        _gatewayEventService = gatewayEventService;
    }

    public async Task<IEnumerable<RouteConfig>> GetRoutesAsync()
    {
        return await _apiDbService.GetRoutesAsync();
    }

    public async Task<RouteConfig?> GetRouteAsync(string id)
    {
        return await _apiDbService.GetRouteAsync(id);
    }

    public async Task<RouteConfig> CreateRouteAsync(RouteConfig route)
    {
        var result = await _apiDbService.CreateRouteAsync(route);
        await NotifyGatewayConfigChangeAsync();
        await NotifyRoutesChangedAsync();
        return result;
    }

    public async Task<RouteConfig?> UpdateRouteAsync(string id, RouteConfig route)
    {
        var result = await _apiDbService.UpdateRouteAsync(id, route);
        await NotifyGatewayConfigChangeAsync();
        await NotifyRoutesChangedAsync();
        return result;
    }

    public async Task DeleteRouteAsync(string id)
    {
        await _apiDbService.DeleteRouteAsync(id);
        await NotifyGatewayConfigChangeAsync();
        await NotifyRoutesChangedAsync();
    }

    public async Task<IEnumerable<ClusterConfig>> GetClustersAsync()
    {
        return await _apiDbService.GetClustersAsync();
    }

    public async Task<ClusterConfig?> GetClusterAsync(string id)
    {
        return await _apiDbService.GetClusterAsync(id);
    }

    public async Task<ClusterConfig> CreateClusterAsync(ClusterConfig cluster)
    {
        var result = await _apiDbService.CreateClusterAsync(cluster);
        await NotifyGatewayConfigChangeAsync();
        return result;
    }

    public async Task<ClusterConfig?> UpdateClusterAsync(string id, ClusterConfig cluster)
    {
        var result = await _apiDbService.UpdateClusterAsync(id, cluster);
        await NotifyGatewayConfigChangeAsync();
        return result;
    }

    public async Task DeleteClusterAsync(string id)
    {
        await _apiDbService.DeleteClusterAsync(id);
        await NotifyGatewayConfigChangeAsync();
    }

    public async Task<ClusterConfig?> AddDestinationAsync(string clusterId, DestinationConfig destination)
    {
        var result = await _apiDbService.AddDestinationAsync(clusterId, destination);
        await NotifyGatewayConfigChangeAsync();
        return result;
    }

    public async Task<ClusterConfig?> RemoveDestinationAsync(string clusterId, string destinationId)
    {
        var result = await _apiDbService.RemoveDestinationAsync(clusterId, destinationId);
        await NotifyGatewayConfigChangeAsync();
        return result;
    }

    private async Task InitializeSampleDataAsync()
    {
        await _apiDbService.InitializeSampleDataAsync();
    }

    /// <summary>
    /// 通知网关配置变更
    /// </summary>
    /// <returns>任务</returns>
    private async Task NotifyGatewayConfigChangeAsync()
    {
        try
        {
            // 发送事件到所有网关实例
            await _gatewayEventService.SendEventToAllInstancesAsync(GatewayEventType.ConfigUpdate, "{}", 1);
            _logger.LogInformation("Sent config update notification to all gateways");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify gateway of config change");
        }
    }

    /// <summary>
    /// 通知路由变更（触发证书登记对账）。与 ConfigUpdate 分开：
    /// 后者也由证书/集群变更触发，复用会导致每次证书同步都触发一次无意义对账。
    /// </summary>
    private async Task NotifyRoutesChangedAsync()
    {
        try
        {
            await _gatewayEventService.SendEventToAllInstancesAsync(GatewayEventType.RoutesChanged, "{}", 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify route change for certificate enrollment reconcile");
        }
    }

    public async Task<IEnumerable<CertificateConfig>> GetCertificatesAsync()
    {
        return await _apiDbService.GetCertificatesAsync();
    }

    public async Task<CertificateConfig?> GetCertificateAsync(string id)
    {
        return await _apiDbService.GetCertificateAsync(id);
    }

    public async Task<CertificateConfig> CreateCertificateAsync(CertificateConfig certificate)
    {
        var result = await _apiDbService.CreateCertificateAsync(certificate);
        await NotifyGatewayConfigChangeAsync();
        return result;
    }

    public async Task<CertificateConfig?> UpdateCertificateAsync(string id, CertificateConfig certificate)
    {
        var result = await _apiDbService.UpdateCertificateAsync(id, certificate);
        await NotifyGatewayConfigChangeAsync();
        return result;
    }

    public async Task DeleteCertificateAsync(string id)
    {
        await _apiDbService.DeleteCertificateAsync(id);
        await NotifyGatewayConfigChangeAsync();
    }

    /// <summary>
    /// 批量删除某登记导入的证书（级联清理）：批量落库一次、广播一次。
    /// </summary>
    public async Task<int> DeleteCertificatesByEnrollmentAsync(string enrollmentId)
    {
        var deleted = await _apiDbService.DeleteCertificatesByEnrollmentAsync(enrollmentId);
        if (deleted > 0)
        {
            await NotifyGatewayConfigChangeAsync();
        }

        return deleted;
    }
}