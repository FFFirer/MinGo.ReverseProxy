using MinGo.Messaging;

namespace MinGo.Core.Messaging;

/// <summary>
/// 证书申请完成事件（与 MinGo.CertManager.Core.Messaging.CertificateCompletedEvent 同构）。
/// 契约 Id 决定 broker topic，字段名与序列化（JSON camelCase）必须与发布方完全一致。
/// </summary>
[MessageContract(Id = "certmanager.certificate.completed", Version = "1", Kind = MessageKind.Event)]
public class CertificateCompletedEvent : IEvent
{
    public Guid CertificateId { get; set; }

    public string Domain { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// 证书申请失败事件（与 MinGo.CertManager.Core.Messaging.CertificateFailedEvent 同构）。
/// </summary>
[MessageContract(Id = "certmanager.certificate.failed", Version = "1", Kind = MessageKind.Event)]
public class CertificateFailedEvent : IEvent
{
    public Guid CertificateId { get; set; }

    public string Domain { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;
}
