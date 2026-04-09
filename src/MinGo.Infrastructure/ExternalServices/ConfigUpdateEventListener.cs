using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;

namespace MinGo.Infrastructure.ExternalServices;

/// <summary>
/// 配置更新事件监听器
/// </summary>
public class ConfigUpdateEventListener : IHostedService, IDisposable
{
    private readonly ILogger<ConfigUpdateEventListener> _logger;
    private readonly IServiceProvider _serviceProvider;
    private string _subscriptionId = string.Empty;
    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="serviceProvider">服务提供程序</param>
    public ConfigUpdateEventListener(ILogger<ConfigUpdateEventListener> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 启动服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("配置更新事件监听器启动中...");

        using var scope = _serviceProvider.CreateScope();
        var messageNotificationService = scope.ServiceProvider.GetRequiredService<IMessageNotificationService>();

        // 订阅配置更新事件
        _subscriptionId = await messageNotificationService.SubscribeAsync(
            "ConfigUpdateEventListener",
            new[] { GatewayEventType.ConfigUpdate },
            async (gatewayEvent) => await HandleConfigUpdateEventAsync(gatewayEvent));

        _logger.LogInformation("配置更新事件监听器已启动，订阅ID: {SubscriptionId}", _subscriptionId);
    }

    /// <summary>
    /// 处理配置更新事件
    /// </summary>
    /// <param name="gatewayEvent">网关事件</param>
    /// <returns>任务</returns>
    private async Task HandleConfigUpdateEventAsync(GatewayEvent gatewayEvent)
    {
        _logger.LogInformation("收到配置更新事件: {EventId}, 类型: {EventType}", gatewayEvent.EventId, gatewayEvent.EventType);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var proxyConfigProvider = scope.ServiceProvider.GetRequiredService<DatabaseProxyConfigProvider>();

            // 异步刷新配置
            await proxyConfigProvider.RefreshAsync();
            _logger.LogInformation("配置已成功刷新");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理配置更新事件时发生错误");
        }
    }

    /// <summary>
    /// 停止服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("配置更新事件监听器停止中...");

        using var scope = _serviceProvider.CreateScope();
        var messageNotificationService = scope.ServiceProvider.GetRequiredService<IMessageNotificationService>();

        // 取消订阅
        if (!string.IsNullOrEmpty(_subscriptionId))
        {
            await messageNotificationService.UnsubscribeAsync(_subscriptionId);
            _logger.LogInformation("配置更新事件订阅已取消");
        }

        _logger.LogInformation("配置更新事件监听器已停止");
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
