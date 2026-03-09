using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MinGo.Gateway.Options;
using MinGo.Gateway.Services;
using MinGo.Shared.Models;
using System.Text.Json;
using Yarp.ReverseProxy.Configuration;

namespace MinGo.Gateway.Controllers;

/// <summary>
/// 事件接收控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    /// <summary>
    /// 接收事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>事件响应</returns>
    [HttpPost]
    public async Task<ActionResult<GatewayEventResponse>> ReceiveEvent([FromBody] GatewayEvent gatewayEvent)
    {
        try
        {
            _logger.LogDebug("接收到事件: EventId={EventId}, EventType={EventType}, Priority={Priority}", 
                gatewayEvent.EventId, gatewayEvent.EventType, gatewayEvent.Priority);
            _logger.LogDebug("事件数据: {EventData}", gatewayEvent.EventData);
            
            // 处理事件
            await ProcessEventAsync(gatewayEvent);

            // 返回成功响应
            var response = new GatewayEventResponse
            {
                EventId = gatewayEvent.EventId,
                Success = true,
                InstanceId = Environment.MachineName // 使用机器名作为实例ID
            };

            _logger.LogDebug("事件处理成功，返回响应: {Response}", JsonSerializer.Serialize(response));
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "事件处理失败: {EventId}", gatewayEvent.EventId);
            
            // 返回错误响应
            var response = new GatewayEventResponse
            {
                EventId = gatewayEvent.EventId,
                Success = false,
                ErrorMessage = ex.Message,
                InstanceId = Environment.MachineName
            };

            _logger.LogDebug("返回错误响应: {Response}", JsonSerializer.Serialize(response));
            return BadRequest(response);
        }
    }

    private readonly DatabaseProxyConfigProvider _configProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ControlPlaneOptions _controlPlaneOptions;
    private readonly ILogger<EventsController> _logger;

    public EventsController(
        DatabaseProxyConfigProvider configProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<ControlPlaneOptions> controlPlaneOptions,
        ILogger<EventsController> logger)
    {
        _configProvider = configProvider;
        _httpClientFactory = httpClientFactory;
        _controlPlaneOptions = controlPlaneOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// 处理事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>任务</returns>
    private async Task ProcessEventAsync(GatewayEvent gatewayEvent)
    {
        _logger.LogDebug("开始处理事件: {EventType} {EventId}", gatewayEvent.EventType, gatewayEvent.EventId);
        // 根据事件类型处理不同的事件
        switch (gatewayEvent.EventType)
        {
            case GatewayEventType.ConfigUpdate:
                await HandleConfigUpdateEventAsync(gatewayEvent);
                break;
            case GatewayEventType.CertificateUpdate:
                await HandleCertificateUpdateEventAsync(gatewayEvent);
                break;
            case GatewayEventType.RouteUpdate:
                await HandleRouteUpdateEventAsync(gatewayEvent);
                break;
            case GatewayEventType.ClusterUpdate:
                await HandleClusterUpdateEventAsync(gatewayEvent);
                break;
            case GatewayEventType.Restart:
                await HandleRestartEventAsync(gatewayEvent);
                break;
            case GatewayEventType.Shutdown:
                await HandleShutdownEventAsync(gatewayEvent);
                break;
            case GatewayEventType.HealthCheck:
                await HandleHealthCheckEventAsync(gatewayEvent);
                break;
            case GatewayEventType.Custom:
                await HandleCustomEventAsync(gatewayEvent);
                break;
        }
    }

    /// <summary>
    /// 处理配置更新事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>任务</returns>
    private async Task HandleConfigUpdateEventAsync(GatewayEvent gatewayEvent)
    {
        try
        {
            _logger.LogInformation($"处理配置更新事件: {gatewayEvent.EventId}");

            // 从数据库刷新配置
            _configProvider.Refresh();
            _logger.LogInformation("成功从数据库刷新配置");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理配置更新事件失败");
            throw;
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 从ControlPlane拉取最新的配置
    /// </summary>
    /// <returns>网关配置</returns>
    private async Task<GatewayConfig?> FetchLatestConfigAsync()
    {
        try
        {
            var controlPlaneUrl = _controlPlaneOptions.GetConfigApiUrl();
            _logger.LogInformation("正在从 {Url} 拉取配置", controlPlaneUrl);

            var httpClient = _httpClientFactory.CreateClient("ControlPlane");
            var response = await httpClient.GetAsync(controlPlaneUrl);

            if (response.IsSuccessStatusCode)
            {
                var config = await response.Content.ReadFromJsonAsync<GatewayConfig>();
                _logger.LogInformation("成功拉取最新配置");
                return config;
            }
            else
            {
                _logger.LogWarning("拉取配置失败: {StatusCode}", response.StatusCode);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "拉取配置异常");
            return null;
        }
    }

    /// <summary>
    /// 处理证书更新事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>任务</returns>
    private async Task HandleCertificateUpdateEventAsync(GatewayEvent gatewayEvent)
    {
        // 处理证书更新逻辑
        _logger.LogInformation($"处理证书更新事件: {gatewayEvent.EventId}");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理路由更新事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>任务</returns>
    private async Task HandleRouteUpdateEventAsync(GatewayEvent gatewayEvent)
    {
        // 处理路由更新逻辑
        _logger.LogInformation($"处理路由更新事件: {gatewayEvent.EventId}");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理集群更新事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>任务</returns>
    private async Task HandleClusterUpdateEventAsync(GatewayEvent gatewayEvent)
    {
        // 处理集群更新逻辑
        _logger.LogInformation($"处理集群更新事件: {gatewayEvent.EventId}");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理重启事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>任务</returns>
    private async Task HandleRestartEventAsync(GatewayEvent gatewayEvent)
    {
        // 处理重启逻辑
        _logger.LogInformation($"处理重启事件: {gatewayEvent.EventId}");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理下线事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>任务</returns>
    private async Task HandleShutdownEventAsync(GatewayEvent gatewayEvent)
    {
        // 处理下线逻辑
        _logger.LogInformation($"处理下线事件: {gatewayEvent.EventId}");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理健康检查事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>任务</returns>
    private async Task HandleHealthCheckEventAsync(GatewayEvent gatewayEvent)
    {
        // 处理健康检查逻辑
        _logger.LogInformation($"处理健康检查事件: {gatewayEvent.EventId}");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理自定义事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>任务</returns>
    private async Task HandleCustomEventAsync(GatewayEvent gatewayEvent)
    {
        // 处理自定义事件逻辑
        _logger.LogInformation($"处理自定义事件: {gatewayEvent.EventId}");
        await Task.CompletedTask;
    }
}
