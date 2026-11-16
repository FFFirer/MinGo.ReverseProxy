namespace MinGo.Core.Interfaces;

/// <summary>
/// Cloudflare 接入配置管理服务（单例行，设置页编辑）。
/// </summary>
public interface ICloudflareSettingsService
{
    /// <summary>读取配置，ApiToken 返回脱敏值。</summary>
    Task<CloudflareSettingsDto> GetAsync();

    /// <summary>读取配置并返回完整 ApiToken，仅供服务端调用。</summary>
    Task<CloudflareSettingsDto> GetWithTokenAsync();

    /// <summary>
    /// 保存配置。ApiToken 为空白或脱敏值（含 ***）时保留现有值（新建时保持为空，由配置节兜底）。
    /// </summary>
    Task<CloudflareSettingsDto> SaveAsync(CloudflareSettingsDto dto);
}

/// <summary>
/// Cloudflare 接入配置 DTO。ApiToken 读取时脱敏，写入时接受明文。
/// </summary>
public sealed class CloudflareSettingsDto
{
    public string ApiToken { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>是否已配置可用 Token（配置节兜底也算）。</summary>
    public bool Configured { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}