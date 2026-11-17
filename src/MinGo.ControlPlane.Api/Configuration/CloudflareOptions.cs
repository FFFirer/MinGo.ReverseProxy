namespace MinGo.ControlPlane.Api.Configuration;

/// <summary>
/// Cloudflare 连接默认配置。ApiToken/AccountId/TimeoutSeconds 以设置页保存值优先，
/// 未保存时回退到本配置节；DataPlaneOrigin/TunnelOrigins/DefaultCatchAll 始终来自配置节（部署拓扑相关）。
/// </summary>
public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    /// <summary>
    /// 生效回源地址来源：全局默认值。
    /// </summary>
    public const string OriginSourceDefault = "default";

    /// <summary>
    /// 生效回源地址来源：<see cref="TunnelOrigins"/> 按隧道名命中。
    /// </summary>
    public const string OriginSourceTunnelOverride = "tunnel-override";

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
    /// 隧道 ingress 的默认网关回源地址，适用于所有未单独覆盖的隧道。
    /// 地址由 cloudflared 在自身网络命名空间解析，因此必须是连接器可达的地址：
    /// compose 服务名（如 <c>http://data-plane:8080</c>）只在 cloudflared 加入同一
    /// user-defined bridge/overlay 网络时成立，否则应写宿主地址 + 发布端口。
    /// </summary>
    public string DataPlaneOrigin { get; set; } = "http://data-plane:8080";

    /// <summary>
    /// 按隧道名覆盖回源地址，用于连接器分布在不同网络范围的部署。
    /// 环境变量形如 <c>Cloudflare__TunnelOrigins__0__TunnelName=home</c>。
    /// </summary>
    public List<CloudflareTunnelOrigin> TunnelOrigins { get; set; } = [];

    /// <summary>
    /// ingress 末尾兜底规则（无 hostname 的最后一条），保证未匹配主机名得到明确响应。
    /// </summary>
    public string DefaultCatchAll { get; set; } = "http_status:404";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiToken);

    /// <summary>
    /// 解析某隧道的生效回源地址：隧道名命中 <see cref="TunnelOrigins"/> 时用覆盖值，
    /// 否则回退 <see cref="DataPlaneOrigin"/>。隧道名为空或未配置覆盖时不会失败，
    /// 保证单隧道部署无需任何额外配置。
    /// </summary>
    public ResolvedOrigin ResolveOrigin(string? tunnelName)
    {
        var name = (tunnelName ?? string.Empty).Trim();
        if (name.Length > 0)
        {
            var match = (TunnelOrigins ?? [])
                .FirstOrDefault(o =>
                    !string.IsNullOrWhiteSpace(o.TunnelName)
                    && !string.IsNullOrWhiteSpace(o.Origin)
                    && string.Equals(o.TunnelName.Trim(), name, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return new ResolvedOrigin(match.Origin.Trim(), OriginSourceTunnelOverride);
        }

        return new ResolvedOrigin(DataPlaneOrigin, OriginSourceDefault);
    }

    /// <summary>
    /// 声明某隧道的生效回源地址及其来源，供对账与 UI 说明「为什么是这个地址」。
    /// </summary>
    public sealed record ResolvedOrigin(string Origin, string Source);
}

/// <summary>
/// 隧道级回源地址覆盖项。以隧道名为键（用户在 Cloudflare 侧起的可读名称，隧道 ID 为 UUID 不适合手写）。
/// </summary>
public sealed class CloudflareTunnelOrigin
{
    public string TunnelName { get; set; } = string.Empty;

    /// <summary>
    /// 该隧道连接器可达的回源地址，形如 <c>http://192.168.88.134:8080</c>。
    /// </summary>
    public string Origin { get; set; } = string.Empty;
}
