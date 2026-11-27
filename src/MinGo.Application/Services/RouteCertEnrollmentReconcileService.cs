using Microsoft.Extensions.Logging;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.Application.Services;

/// <summary>
/// 路由驱动证书登记对账：从数据面路由派生域名，确保其都有证书接入登记。
/// 只增不删——仅 UI 登记的域名（未被路由引用）完整保留；移除登记只走人工。
/// </summary>
public class RouteCertEnrollmentReconcileService
{
    private readonly CertificateEnrollmentService _enrollments;
    private readonly ICertificateEnrollmentStore _store;
    private readonly IApiManagementService _apiManagement;
    private readonly ICertManagerSettingsService _settings;
    private readonly ILogger<RouteCertEnrollmentReconcileService> _logger;

    public RouteCertEnrollmentReconcileService(
        CertificateEnrollmentService enrollments,
        ICertificateEnrollmentStore store,
        IApiManagementService apiManagement,
        ICertManagerSettingsService settings,
        ILogger<RouteCertEnrollmentReconcileService> logger)
    {
        _enrollments = enrollments;
        _store = store;
        _apiManagement = apiManagement;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// 对账一次：收集数据面域名 → 无登记者自动登记 → 泛域漂移升级。
    /// 设置未启用/未配置时直接跳过（避免对每个新域名生成 Failed 登记）。
    /// </summary>
    public async Task ReconcileAsync(CancellationToken ct)
    {
        var settings = await _settings.GetAsync();
        if (!settings.Enabled || !settings.Configured)
        {
            _logger.LogDebug("CertManager 未启用或未配置，跳过路由域名自动登记");
            return;
        }

        var routes = await _apiManagement.GetRoutesAsync();
        var hosts = RouteCertEnrollmentPolicy.CollectHosts(routes);
        if (hosts.Count == 0)
        {
            return;
        }

        var enrollments = await _store.GetAllAsync();
        var byDomain = enrollments.ToDictionary(
            e => CertificateEnrollmentPolicy.NormalizeDomain(e.Domain),
            StringComparer.Ordinal);

        foreach (var (domain, isWildcard) in hosts)
        {
            if (!byDomain.TryGetValue(domain, out var existing))
            {
                await RegisterAsync(domain, isWildcard, ct);
                continue;
            }

            if (!existing.Enabled)
            {
                continue;
            }

            if (isWildcard && !existing.IsWildcard)
            {
                existing.IsWildcard = true;
                await _store.UpdateAsync(existing.Id, existing);
                _logger.LogInformation("路由要求泛域名，登记升级为泛域: Domain={Domain}", domain);
            }
        }
    }

    private async Task RegisterAsync(string domain, bool isWildcard, CancellationToken ct)
    {
        try
        {
            await _enrollments.RegisterAsync(new RegisterCertificateEnrollmentRequest
            {
                Domain = domain,
                IsWildcard = isWildcard,
                UseStaging = false,
            }, ct);
            _logger.LogInformation(
                "路由域名自动登记: Domain={Domain}, Wildcard={Wildcard}", domain, isWildcard);
        }
        catch (EnrollmentException)
        {
            // 并发对账/人工登记抢先创建；唯一索引兜底，视为已登记
            _logger.LogDebug("路由域名已存在登记，跳过: Domain={Domain}", domain);
        }
        catch (Exception ex)
        {
            // 不中断整轮对账；客户端错误已由 RegisterAsync 落登记 Failed 状态
            _logger.LogError(ex, "路由域名自动登记失败: Domain={Domain}", domain);
        }
    }
}