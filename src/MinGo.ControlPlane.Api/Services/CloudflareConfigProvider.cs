using Microsoft.Extensions.Options;
using MinGo.ControlPlane.Api.Configuration;
using MinGo.Core.Interfaces;

namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// 解析 Cloudflare 生效配置：数据库（设置页保存）优先，未保存时回退到配置节默认值。
/// 作用域内缓存，一次请求最多读一次库。
/// </summary>
public sealed class CloudflareConfigProvider
{
    private readonly ICloudflareSettingsService _settings;
    private readonly CloudflareOptions _defaults;
    private CloudflareOptions? _resolved;

    public CloudflareConfigProvider(
        ICloudflareSettingsService settings,
        IOptions<CloudflareOptions> defaults)
    {
        _settings = settings;
        _defaults = defaults.Value;
    }

    public async Task<CloudflareOptions> GetAsync(CancellationToken ct = default)
    {
        if (_resolved is not null)
            return _resolved;

        var stored = await _settings.GetWithTokenAsync();
        _resolved = stored.UpdatedAt is null
            ? _defaults
            : new CloudflareOptions
            {
                ApiToken = string.IsNullOrWhiteSpace(stored.ApiToken) ? _defaults.ApiToken : stored.ApiToken,
                AccountId = stored.AccountId,
                TimeoutSeconds = stored.TimeoutSeconds,
                DataPlaneOrigin = _defaults.DataPlaneOrigin,
                TunnelOrigins = _defaults.TunnelOrigins,
                DefaultCatchAll = _defaults.DefaultCatchAll,
            };

        return _resolved;
    }
}