namespace MinGo.Core.Entities;

/// <summary>
/// Cloudflare 接入配置（单行）。API Token 由设置页写入，读取时脱敏。
/// </summary>
public class CloudflareSettingsEntity
{
    public const string DefaultId = "default";

    public string Id { get; set; } = DefaultId;

    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// 留空时自动从 GET /accounts 发现（账号唯一时）。
    /// </summary>
    public string AccountId { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}