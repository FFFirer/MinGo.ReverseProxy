namespace MinGo.ControlPlane.Api.Configuration;

/// <summary>
/// Portainer 全局默认配置。实例级别配置存储在数据库中，通过页面管理。
/// </summary>
public sealed class PortainerOptions
{
    public const string SectionName = "Portainer";

    /// <summary>
    /// 全局默认请求超时秒数（实例可覆盖）。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// 全局默认发现缓存秒数（实例可覆盖）。
    /// </summary>
    public int CacheSeconds { get; set; } = 30;
}
