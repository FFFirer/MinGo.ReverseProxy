using MinGo.ControlPlane.Data;
using MinGo.Shared.Models;

namespace MinGo.ControlPlane.Services;

public class ApiManagementService : IApiManagementService
{
    private readonly ILogger<ApiManagementService> _logger;
    private readonly IApiDbService _apiDbService;

    public ApiManagementService(ILogger<ApiManagementService> logger, IApiDbService apiDbService)
    {
        _logger = logger;
        _apiDbService = apiDbService;
        
        // 初始化示例数据
        _ = InitializeSampleDataAsync();
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
        return await _apiDbService.CreateRouteAsync(route);
    }

    public async Task<RouteConfig?> UpdateRouteAsync(string id, RouteConfig route)
    {
        return await _apiDbService.UpdateRouteAsync(id, route);
    }

    public async Task DeleteRouteAsync(string id)
    {
        await _apiDbService.DeleteRouteAsync(id);
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
        return await _apiDbService.CreateClusterAsync(cluster);
    }

    public async Task<ClusterConfig?> UpdateClusterAsync(string id, ClusterConfig cluster)
    {
        return await _apiDbService.UpdateClusterAsync(id, cluster);
    }

    public async Task DeleteClusterAsync(string id)
    {
        await _apiDbService.DeleteClusterAsync(id);
    }

    public async Task<ClusterConfig?> AddDestinationAsync(string clusterId, string destinationId, DestinationConfig destination)
    {
        return await _apiDbService.AddDestinationAsync(clusterId, destinationId, destination);
    }

    public async Task<ClusterConfig?> UpdateDestinationAsync(string clusterId, string destinationId, DestinationConfig destination)
    {
        return await _apiDbService.UpdateDestinationAsync(clusterId, destinationId, destination);
    }

    public async Task<ClusterConfig?> RemoveDestinationAsync(string clusterId, string destinationId)
    {
        return await _apiDbService.RemoveDestinationAsync(clusterId, destinationId);
    }

    private async Task InitializeSampleDataAsync()
    {
        await _apiDbService.InitializeSampleDataAsync();
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
