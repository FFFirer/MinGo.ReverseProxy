namespace MinGo.ControlPlane.Api.Configuration;

/// <summary>
/// Cloudflare 连接默认配置。ApiToken/AccountId/TimeoutSeconds 以设置页保存值优先，
/// 未保存时回退到本配置节；DataPlaneOrigin/DefaultCatchAll 始终来自配置节（部署拓扑相关）。
/// </summary>
public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    /// <summary>
    /// API Token，需权限：Account:Cloudflare Tunnel:Edit、Zone:DNS:Edit、Zone:Zone:Read。
    /// </summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// 留空时自动从 GET /accounts 发现。
    /// </summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>
    /// 单次 API 请求超时秒数。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// 隧道 ingress 的网关回源地址（cloudflared 所在网络可达的数据面地址）。
    /// </summary>
    public string DataPlaneOrigin { get; set; } = "http://data-plane:8080";

    /// <summary>
    /// ingress 末尾兜底规则（无 hostname 的最后一条），保证未匹配主机名得到明确响应。
    /// </summary>
    public string DefaultCatchAll { get; set; } = "http_status:404";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiToken);
}
