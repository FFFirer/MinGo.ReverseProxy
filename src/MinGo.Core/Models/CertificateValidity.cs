using System.Security.Cryptography.X509Certificates;

namespace MinGo.Core.Models;

/// <summary>
/// 证书时效判定——全仓唯一的"在时效内"定义（<c>NotBefore &lt;= now &lt;= NotAfter</c>）。
/// <para>
/// 这个谓词只用于展示与日志，<b>不</b>参与下发过滤或握手选择：
/// 过期证书照常下发、照常完成握手，由客户端拒绝并给出可读原因。
/// </para>
/// </summary>
public static class CertificateValidity
{
    /// <summary>证书自身声明的时效窗口是否包含给定时刻。</summary>
    public static bool IsWithinWindow(X509Certificate2 certificate, DateTimeOffset now)
        => now >= certificate.NotBefore && now <= certificate.NotAfter;

    /// <summary>仅凭存储的到期时间判断（存储层没有 NotBefore）。<c>null</c> 视为长期有效。</summary>
    public static bool HasNotExpired(DateTimeOffset? expiresAt, DateTimeOffset now)
        => expiresAt is not { } value || now <= value;
}
