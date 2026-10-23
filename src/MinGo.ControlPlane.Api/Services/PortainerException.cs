namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// Portainer 调用失败。StatusCode 为建议返回给前端的 HTTP 状态码。
/// </summary>
public sealed class PortainerException : Exception
{
    public int StatusCode { get; }

    public PortainerException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
