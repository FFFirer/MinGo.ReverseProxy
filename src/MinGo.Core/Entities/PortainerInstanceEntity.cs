using System.Text.Json;

namespace MinGo.Core.Entities;

/// <summary>
/// Portainer 实例实体 — 存储在数据库中，支持多实例管理。
/// </summary>
public class PortainerInstanceEntity
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API 密钥（数据库存储，API 响应中脱敏）。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

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
    /// 非空时直接作为自身 stack（JSON 数组）。
    /// </summary>
    public string SelfStackNamesJson { get; set; } = "[]";

    /// <summary>
    /// 主机名未命中时的镜像兜底匹配片段（JSON 数组）。
    /// </summary>
    public string SelfImagePatternsJson { get; set; } = "[\"reverseproxy-cp\"]";

    /// <summary>
    /// 额外隐藏的 compose project（JSON 数组）。
    /// </summary>
    public string ExtraExcludedStacksJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string[] GetSelfStackNames()
    {
        try { return JsonSerializer.Deserialize<string[]>(SelfStackNamesJson) ?? []; }
        catch { return []; }
    }

    public string[] GetSelfImagePatterns()
    {
        try { return JsonSerializer.Deserialize<string[]>(SelfImagePatternsJson) ?? ["reverseproxy-cp"]; }
        catch { return ["reverseproxy-cp"]; }
    }

    public string[] GetExtraExcludedStacks()
    {
        try { return JsonSerializer.Deserialize<string[]>(ExtraExcludedStacksJson) ?? []; }
        catch { return []; }
    }
}
