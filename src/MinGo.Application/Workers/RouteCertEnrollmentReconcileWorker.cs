using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinGo.Application.Services;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;

namespace MinGo.Application.Workers;

/// <summary>
/// 路由变更 → 证书登记对账（事件总线订阅者）。
/// 事件仅作信号：对账以"当前路由全量"为准，重复/乱序/丢失均无状态问题。
/// 作用域纪律：构造器只注入根 <see cref="IServiceProvider"/>，每个事件经 CreateScope 解析 Scoped 服务，
/// 禁止把 Scoped 实例捕获进回调闭包（MemoryMessageNotificationService 回调运行在无作用域的裸线程上）。
/// </summary>
public class RouteCertEnrollmentReconcileWorker : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RouteCertEnrollmentReconcileWorker> _logger;
    private string _subscriptionId = string.Empty;
    private int _reconciling;

    public RouteCertEnrollmentReconcileWorker(
        IServiceProvider serviceProvider,
        ILogger<RouteCertEnrollmentReconcileWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageNotificationService>();
        _subscriptionId = await bus.SubscribeAsync(
            nameof(RouteCertEnrollmentReconcileWorker),
            new[] { GatewayEventType.RoutesChanged },
            _ => HandleRoutesChangedAsync());

        _logger.LogInformation(
            "路由证书登记对账已订阅 RoutesChanged，subscription: {Id}", _subscriptionId);

        // 启动兜底：存量路由补登记（事件丢失窗口的收敛路径之一）
        await ReconcileOnceAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageNotificationService>();
        if (!string.IsNullOrEmpty(_subscriptionId))
        {
            await bus.UnsubscribeAsync(_subscriptionId);
        }

        _logger.LogInformation("路由证书登记对账已退订");
    }

    private async Task HandleRoutesChangedAsync()
    {
        // 在途合并：对账永远读最新路由，运行中跳过本次事件无状态损失
        if (Interlocked.CompareExchange(ref _reconciling, 1, 0) != 0)
        {
            _logger.LogDebug("路由登记对账进行中，跳过本次事件");
            return;
        }

        try
        {
            await ReconcileOnceAsync(CancellationToken.None);
        }
        finally
        {
            Interlocked.Exchange(ref _reconciling, 0);
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RouteCertEnrollmentReconcileService>();
            await service.ReconcileAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "路由证书登记对账失败（不中断事件处理）");
        }
    }
}