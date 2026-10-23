namespace MinGo.ControlPlane.Api.Configuration;

/// <summary>
/// Portainer 只读发现配置。凭据只允许来自配置/环境变量，不落库、不回显。
/// </summary>
public sealed class PortainerOptions
{
    public const string SectionName = "Portainer";

    public bool Enabled { get; set; }

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    public int CacheSeconds { get; set; } = 30;

    /// <summary>
    /// 容器发布端口绑定在 0.0.0.0 / :: 时使用的宿主地址。
    /// </summary>
    public string? HostAddress { get; set; }

    /// <summary>
    /// 自身 data-plane 的 compose service 名。
    /// </summary>
    public string DataPlaneService { get; set; } = "data-plane";

    /// <summary>
    /// 非空时直接作为自身 stack（人工覆盖自动识别）。
    /// </summary>
    public string[] SelfStackNames { get; set; } = [];

    /// <summary>
    /// 主机名未命中时的镜像兜底匹配片段。
    /// </summary>
    public string[] SelfImagePatterns { get; set; } = ["reverseproxy-cp"];

    /// <summary>
    /// 额外隐藏的 compose project（例如同机遗留部署）。
    /// </summary>
    public string[] ExtraExcludedStacks { get; set; } = [];

    public bool ApiKeyConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(BaseUrl) && ApiKeyConfigured;

    public Uri? TryGetBaseUri()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) return null;
        return Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) ? uri : null;
    }
}
