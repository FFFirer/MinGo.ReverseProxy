namespace MinGo.Gateway.Options;

/// <summary>
/// 控制平面配置选项
/// </summary>
public class ControlPlaneOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "ControlPlane";

    /// <summary>
    /// 控制平面基础URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 获取配置API的路径
    /// </summary>
    public string ConfigApiPath { get; set; } = "/api/config/latest";

    /// <summary>
    /// 请求超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 是否跳过SSL证书验证（仅开发环境使用）
    /// </summary>
    public bool SkipSslCertificateValidation { get; set; } = false;

    /// <summary>
    /// 获取完整的配置API URL
    /// </summary>
    public string GetConfigApiUrl()
    {
        return Url.TrimEnd('/') + ConfigApiPath;
    }
}
