namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// Cloudflare API 调用失败，携带 HTTP 状态码与 CF 侧错误信息。
/// </summary>
public sealed class CloudflareException : Exception
{
    public int? StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public CloudflareException(string message, int? statusCode = null, IEnumerable<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }
}
