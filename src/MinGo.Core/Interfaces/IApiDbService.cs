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
    /// 添加目标（后端自动生成 ID）
    /// </summary>
    /// <param name="clusterId">集群ID</param>
    /// <param name="destination">目标配置（不需传id）</param>
    /// <returns>更新后的集群</returns>
    Task<ClusterConfig?> AddDestinationAsync(string clusterId, DestinationConfig destination);

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
    /// 获取所有数据面节点（含实例数与引用路由数）
    /// </summary>
    Task<IReadOnlyList<DataPlaneNode>> GetNodesAsync();

    /// <summary>
    /// 根据ID获取节点
    /// </summary>
    Task<DataPlaneNode?> GetNodeAsync(string id);

    /// <summary>
    /// 创建节点（ID 由后端生成）
    /// </summary>
    Task<DataPlaneNode> CreateNodeAsync(string name);

    /// <summary>
    /// 重命名节点；不存在返回 null
    /// </summary>
    Task<DataPlaneNode?> RenameNodeAsync(string id, string name);

    /// <summary>
    /// 删除节点。被路由引用时拒绝（Deleted=false 并列出引用路由名）；节点不存在返回 null。
    /// 删除会同时清除该节点下的实例绑定（实例回到未绑定）。
    /// </summary>
    Task<NodeDeleteResult?> DeleteNodeAsync(string id);

    /// <summary>
    /// 幂等确保默认节点（Id=<see cref="ConnectionIds.DefaultNode"/>）存在
    /// </summary>
    Task EnsureDefaultNodeAsync();

    /// <summary>
    /// 获取实例的节点绑定（未绑定返回 null）
    /// </summary>
    Task<InstanceNodeAssignment?> GetInstanceNodeAsync(string instanceId);

    /// <summary>
    /// 设置实例节点绑定（nodeId 为 null/空白表示解绑）
    /// </summary>
    Task SetInstanceNodeAsync(string instanceId, string? nodeId);

    /// <summary>
    /// 初始化示例数据
    /// </summary>
    Task InitializeSampleDataAsync();
}