using MinGo.Core.Models;

namespace MinGo.Core.Interfaces;

/// <summary>
/// API数据库服务接口
/// </summary>
public interface IApiDbService
{
    /// <summary>
    /// 获取所有路由
    /// </summary>
    /// <returns>路由列表</returns>
    Task<IEnumerable<RouteConfig>> GetRoutesAsync();

    /// <summary>
    /// 根据ID获取路由
    /// </summary>
    /// <param name="id">路由ID</param>
    /// <returns>路由配置</returns>
    Task<RouteConfig?> GetRouteAsync(string id);

    /// <summary>
    /// 创建路由
    /// </summary>
    /// <param name="route">路由配置</param>
    /// <returns>创建的路由</returns>
    Task<RouteConfig> CreateRouteAsync(RouteConfig route);

    /// <summary>
    /// 更新路由
    /// </summary>
    /// <param name="id">路由ID</param>
    /// <param name="route">路由配置</param>
    /// <returns>更新后的路由</returns>
    Task<RouteConfig?> UpdateRouteAsync(string id, RouteConfig route);

    /// <summary>
    /// 删除路由
    /// </summary>
    /// <param name="id">路由ID</param>
    Task DeleteRouteAsync(string id);

    /// <summary>
    /// 获取所有集群
    /// </summary>
    /// <returns>集群列表</returns>
    Task<IEnumerable<ClusterConfig>> GetClustersAsync();

    /// <summary>
    /// 根据ID获取集群
    /// </summary>
    /// <param name="id">集群ID</param>
    /// <returns>集群配置</returns>
    Task<ClusterConfig?> GetClusterAsync(string id);

    /// <summary>
    /// 创建集群
    /// </summary>
    /// <param name="cluster">集群配置</param>
    /// <returns>创建的集群</returns>
    Task<ClusterConfig> CreateClusterAsync(ClusterConfig cluster);

    /// <summary>
    /// 更新集群
    /// </summary>
    /// <param name="id">集群ID</param>
    /// <param name="cluster">集群配置</param>
    /// <returns>更新后的集群</returns>
    Task<ClusterConfig?> UpdateClusterAsync(string id, ClusterConfig cluster);

    /// <summary>
    /// 删除集群
    /// </summary>
    /// <param name="id">集群ID</param>
    Task DeleteClusterAsync(string id);

    /// <summary>
    /// 添加目标
    /// </summary>
    /// <param name="clusterId">集群ID</param>
    /// <param name="destinationId">目标ID</param>
    /// <param name="destination">目标配置</param>
    /// <returns>更新后的集群</returns>
    Task<ClusterConfig?> AddDestinationAsync(string clusterId, string destinationId, DestinationConfig destination);

    /// <summary>
    /// 更新目标
    /// </summary>
    /// <param name="clusterId">集群ID</param>
    /// <param name="destinationId">目标ID</param>
    /// <param name="destination">目标配置</param>
    /// <returns>更新后的集群</returns>
    Task<ClusterConfig?> UpdateDestinationAsync(string clusterId, string destinationId, DestinationConfig destination);

    /// <summary>
    /// 移除目标
    /// </summary>
    /// <param name="clusterId">集群ID</param>
    /// <param name="destinationId">目标ID</param>
    /// <returns>更新后的集群</returns>
    Task<ClusterConfig?> RemoveDestinationAsync(string clusterId, string destinationId);

    /// <summary>
    /// 获取所有证书
    /// </summary>
    /// <returns>证书列表</returns>
    Task<IEnumerable<CertificateConfig>> GetCertificatesAsync();

    /// <summary>
    /// 根据ID获取证书
    /// </summary>
    /// <param name="id">证书ID</param>
    /// <returns>证书配置</returns>
    Task<CertificateConfig?> GetCertificateAsync(string id);

    /// <summary>
    /// 创建证书
    /// </summary>
    /// <param name="certificate">证书配置</param>
    /// <returns>创建的证书</returns>
    Task<CertificateConfig> CreateCertificateAsync(CertificateConfig certificate);

    /// <summary>
    /// 更新证书
    /// </summary>
    /// <param name="id">证书ID</param>
    /// <param name="certificate">证书配置</param>
    /// <returns>更新后的证书</returns>
    Task<CertificateConfig?> UpdateCertificateAsync(string id, CertificateConfig certificate);

    /// <summary>
    /// 删除证书
    /// </summary>
    /// <param name="id">证书ID</param>
    Task DeleteCertificateAsync(string id);

    /// <summary>
    /// 初始化示例数据
    /// </summary>
    Task InitializeSampleDataAsync();
}