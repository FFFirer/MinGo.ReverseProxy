using Microsoft.Extensions.Logging;
using MinGo.Application.Services;
using MinGo.Core.Messaging;
using MinGo.Messaging;
using MinGo.Messaging.Subscriptions;

namespace MinGo.Application.Consumers;

/// <summary>
/// 订阅 CertManager 的证书完成事件（契约 certmanager.certificate.completed）。
/// 事件仅作信号：按域名匹配登记后触发同步（下载最新 PFX → 幂等导入 → 广播）。
/// </summary>
[MessageConsumer(Contract = typeof(CertificateCompletedEvent), Group = "cert-enrollment-cp", DeliveryMode = DeliveryMode.Competing)]
public class CertificateCompletedEventConsumer : IConsumer<CertificateCompletedEvent>
{
    private readonly CertificateEnrollmentService _service;
    private readonly ILogger<CertificateCompletedEventConsumer> _logger;

    public CertificateCompletedEventConsumer(
        CertificateEnrollmentService service,
        ILogger<CertificateCompletedEventConsumer> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task ConsumeAsync(ConsumeContext<CertificateCompletedEvent> context, CancellationToken cancellationToken)
    {
        var evt = context.Message;
        _logger.LogInformation(
            "收到证书完成事件: CertificateId={CertificateId}, Domain={Domain}, ExpiresAt={ExpiresAt}",
            evt.CertificateId, evt.Domain, evt.ExpiresAt);

        await _service.HandleCompletedAsync(evt.CertificateId, evt.Domain, cancellationToken);
    }
}

/// <summary>
/// 订阅 CertManager 的证书失败事件（契约 certmanager.certificate.failed）。
/// </summary>
[MessageConsumer(Contract = typeof(CertificateFailedEvent), Group = "cert-enrollment-cp", DeliveryMode = DeliveryMode.Competing)]
public class CertificateFailedEventConsumer : IConsumer<CertificateFailedEvent>
{
    private readonly CertificateEnrollmentService _service;
    private readonly ILogger<CertificateFailedEventConsumer> _logger;

    public CertificateFailedEventConsumer(
        CertificateEnrollmentService service,
        ILogger<CertificateFailedEventConsumer> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task ConsumeAsync(ConsumeContext<CertificateFailedEvent> context, CancellationToken cancellationToken)
    {
        var evt = context.Message;
        _logger.LogWarning(
            "收到证书失败事件: CertificateId={CertificateId}, Domain={Domain}, Error={Error}",
            evt.CertificateId, evt.Domain, evt.ErrorMessage);

        await _service.HandleFailedAsync(evt.Domain, evt.ErrorMessage, cancellationToken);
    }
}
