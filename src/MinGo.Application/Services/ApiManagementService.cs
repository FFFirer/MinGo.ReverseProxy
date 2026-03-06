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
        return result;
    }

    public async Task<RouteConfig?> UpdateRouteAsync(string id, RouteConfig route)
    {
        var result = await _apiDbService.UpdateRouteAsync(id, route);
        await NotifyGatewayConfigChangeAsync();
        return result;
    }

    public async Task DeleteRouteAsync(string id)
    {
        await _apiDbService.DeleteRouteAsync(id);
        await NotifyGatewayConfigChangeAsync();
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

    public async Task<ClusterConfig?> AddDestinationAsync(string clusterId, string destinationId, DestinationConfig destination)
    {
        var result = await _apiDbService.AddDestinationAsync(clusterId, destinationId, destination);
        await NotifyGatewayConfigChangeAsync();
        return result;
    }

    public async Task<ClusterConfig?> UpdateDestinationAsync(string clusterId, string destinationId, DestinationConfig destination)
    {
        var result = await _apiDbService.UpdateDestinationAsync(clusterId, destinationId, destination);
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
        return await _apiDbService.CreateCertificateAsync(certificate);
    }

    public async Task<CertificateConfig?> UpdateCertificateAsync(string id, CertificateConfig certificate)
    {
        return await _apiDbService.UpdateCertificateAsync(id, certificate);
    }

    public async Task DeleteCertificateAsync(string id)
    {
        await _apiDbService.DeleteCertificateAsync(id);
    }
}