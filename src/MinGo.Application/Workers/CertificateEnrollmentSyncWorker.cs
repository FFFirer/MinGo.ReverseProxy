using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinGo.Application.Services;

namespace MinGo.Application.Workers;

/// <summary>
/// 证书接入对账工作器：定时遍历登记，走与事件消费者相同的同步函数。
/// 兜底 SimpleMessageBroker「至多一次」丢事件、broker/CP 宕机窗口、Pending 卡死等场景；
/// 同步失败由服务内部落 LastError，不抛出中断循环。
/// 作用域纪律：宿主服务是单例，构造器只能注入 IServiceScopeFactory，每轮对账独立作用域解析
/// Scoped 的 CertificateEnrollmentService（否则 captive dependency：单例持有终身 AppDbContext，
/// Development 下 ValidateScopes 直接启动失败，Production 下陈旧读 + 变更追踪器无界增长）。
/// </summary>
public class CertificateEnrollmentSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CertificateEnrollmentSyncWorker> _logger;
    private readonly TimeSpan _interval;

    public CertificateEnrollmentSyncWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<CertificateEnrollmentSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var minutes = configuration.GetValue("CertManager:SyncIntervalMinutes", 60);
        _interval = TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 24 * 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("证书接入对账工作器启动，间隔 {Interval}", _interval);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ReconcileAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CertificateEnrollmentService>();

        var enrollments = await service.ListAsync();
        var enabled = enrollments.Where(e => e.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return;
        }

        _logger.LogInformation("开始证书接入对账: Count={Count}", enabled.Count);
        foreach (var enrollment in enabled)
        {
            try
            {
                await service.SyncAsync(enrollment.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "对账异常（已记录，不中断循环）: Domain={Domain}", enrollment.Domain);
            }
        }
    }
}
