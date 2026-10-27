namespace MinGo.DataPlane.Middleware;

/// <summary>
/// HTTP → HTTPS 跳转配置。
/// <para>
/// <see cref="Port"/> 是对外可达端口，只决定 <c>Location</c> 里出现什么值；
/// 实际监听哪些端口由 <c>Kestrel:Endpoints</c> / <c>ASPNETCORE_URLS</c> 决定。
/// </para>
/// </summary>
public sealed class HttpsRedirectOptions
{
    public const string SectionName = "HttpsRedirect";

    /// <summary>默认开启；Development 与本地 compose 通过配置关闭。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>对外可达的 HTTPS 端口（1-65535）。</summary>
    public int Port { get; set; } = 8443;

    /// <summary>有证书但仍需保留明文服务的域名，支持 <c>*.example.com</c> 通配。</summary>
    public string[] HttpOnlyHosts { get; set; } = Array.Empty<string>();

    public bool HasValidPort => Port is >= 1 and <= 65535;

    /// <summary>
    /// 启动自检：内置中间件在端口未知时 fail-open，这里改为启动即失败。
    /// </summary>
    public void Validate()
    {
        if (Enabled && !HasValidPort)
        {
            throw new InvalidOperationException(
                $"HttpsRedirect:Port must be between 1 and 65535 when HttpsRedirect:Enabled is true (got {Port}).");
        }
    }
}
