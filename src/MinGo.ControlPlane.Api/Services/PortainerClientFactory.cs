using System.Collections.Concurrent;
using MinGo.Core.Entities;

namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// 按 Portainer 实例创建/缓存 HttpClient 的工厂。
/// </summary>
public sealed class PortainerClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PortainerClient> _logger;
    private readonly ConcurrentDictionary<string, PortainerClient> _clients = new();

    public PortainerClientFactory(IHttpClientFactory httpClientFactory, ILogger<PortainerClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// 获取或创建指定实例的客户端。实例配置变更后应调用 <see cref="Invalidate"/> 清除缓存。
    /// </summary>
    public PortainerClient GetOrCreate(PortainerInstanceEntity instance)
    {
        return _clients.GetOrAdd(instance.Id, _ =>
            new PortainerClient(_httpClientFactory.CreateClient($"portainer-{instance.Id}"), instance, _logger));
    }

    /// <summary>
    /// 清除指定实例的缓存客户端（配置变更后调用）。
    /// </summary>
    public void Invalidate(string instanceId)
    {
        _clients.TryRemove(instanceId, out _);
    }

    /// <summary>
    /// 清除所有缓存的客户端。
    /// </summary>
    public void InvalidateAll()
    {
        _clients.Clear();
    }
}
