using Microsoft.EntityFrameworkCore;
using MinGo.ControlPlane.Data;
using MinGo.ControlPlane.Services;

namespace MinGo.ControlPlane.Services;

/// <summary>
/// Gateway实例健康检查后台服务
/// </summary>
public class GatewayInstanceHealthCheckService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GatewayInstanceHealthCheckService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="scopeFactory">服务作用域工厂</param>
    /// <param name="logger">日志记录器</param>
    public GatewayInstanceHealthCheckService(
        IServiceScopeFactory scopeFactory,
        ILogger<GatewayInstanceHealthCheckService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// 执行后台任务
    /// </summary>
    /// <param name="stoppingToken">取消令牌</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Gateway instance health check service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var instanceService = scope.ServiceProvider.GetRequiredService<IGatewayInstanceService>();

                // 检查并更新超时实例状态
                var timeoutCount = await instanceService.CheckAndUpdateTimeoutInstancesAsync();
                if (timeoutCount > 0)
                {
                    _logger.LogInformation("Found {Count} instances with heartbeat timeout", timeoutCount);
                }

                // 清理长时间心跳超时的实例
                var cleanupCount = await instanceService.CleanupLongTimeTimeoutInstancesAsync();
                if (cleanupCount > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} instances with long time heartbeat timeout", cleanupCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during gateway instance health check");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Gateway instance health check service stopped");
    }
}
