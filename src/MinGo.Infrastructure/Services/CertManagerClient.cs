using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MinGo.Core.Interfaces;

namespace MinGo.Infrastructure.Services;

/// <summary>
/// CertManager /api/external 客户端。凭据来自落库设置（ICertManagerSettingsService），
/// 每次调用解析一次，设置变更即时生效。
/// </summary>
public class CertManagerClient : ICertManagerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICertManagerSettingsService _settings;

    public CertManagerClient(IHttpClientFactory httpClientFactory, ICertManagerSettingsService settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    /// <summary>发起申请，返回 CertManager 侧证书记录 ID。</summary>
    public async Task<Guid> RequestCertificateAsync(string domain, bool isWildcard, bool useStaging, CancellationToken ct)
    {
        var (client, _) = await CreateClientAsync(ct);
        var response = await client.PostAsJsonAsync(
            "/api/external/certificates",
            new { domain, isWildcard, useStaging },
            JsonOptions, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new CertManagerException("CertManager ApiKey 无效（401）", CertManagerError.Unauthorized);
        }

        if (!response.IsSuccessStatusCode)
        {
            var payload = await ReadErrorAsync(response, ct);
            throw new CertManagerException(payload?.Error ?? $"申请失败（HTTP {(int)response.StatusCode}）", CertManagerError.RequestFailed);
        }

        var result = await response.Content.ReadFromJsonAsync<RequestCertificateResponse>(JsonOptions, ct);
        if (result?.CertificateId is not { } id || id == Guid.Empty)
        {
            throw new CertManagerException("CertManager 响应缺少 CertificateId", CertManagerError.RequestFailed);
        }

        return id;
    }

    /// <summary>查询申请状态；证书不存在返回 null。</summary>
    public async Task<ExternalCertificateStatus?> GetStatusAsync(Guid certificateId, CancellationToken ct)
    {
        var (client, _) = await CreateClientAsync(ct);
        var response = await client.GetAsync($"/api/external/certificates/{certificateId}/status", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new CertManagerException("CertManager ApiKey 无效（401）", CertManagerError.Unauthorized);
        }

        if (!response.IsSuccessStatusCode)
        {
            var payload = await ReadErrorAsync(response, ct);
            throw new CertManagerException(payload?.Error ?? $"查询状态失败（HTTP {(int)response.StatusCode}）", CertManagerError.RequestFailed);
        }

        return await response.Content.ReadFromJsonAsync<ExternalCertificateStatus>(JsonOptions, ct);
    }

    /// <summary>按域名下载最新有效证书（PFX）；域名无证书返回 null。</summary>
    public async Task<byte[]?> DownloadPfxAsync(string domain, string password, CancellationToken ct)
    {
        var (client, _) = await CreateClientAsync(ct);
        var url = $"/api/external/certificates/{Uri.EscapeDataString(domain)}/download?format=Pfx&password={Uri.EscapeDataString(password)}";
        var response = await client.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new CertManagerException("CertManager ApiKey 无效（401）", CertManagerError.Unauthorized);
        }

        if (!response.IsSuccessStatusCode)
        {
            var payload = await ReadErrorAsync(response, ct);
            throw new CertManagerException(payload?.Error ?? $"下载失败（HTTP {(int)response.StatusCode}）", CertManagerError.RequestFailed);
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>探测域名是否已有可下载证书（true=有，false=404 无证书）。</summary>
    public async Task<bool> ProbeHasCertificateAsync(string domain, CancellationToken ct)
    {
        var (client, _) = await CreateClientAsync(ct);
        var url = $"/api/external/certificates/{Uri.EscapeDataString(domain)}/download?format=Pem";
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => true,
            HttpStatusCode.NotFound => false,
            HttpStatusCode.Unauthorized => throw new CertManagerException("CertManager ApiKey 无效（401）", CertManagerError.Unauthorized),
            _ => throw new CertManagerException($"探测失败（HTTP {(int)response.StatusCode}）", CertManagerError.RequestFailed),
        };
    }

    /// <summary>
    /// 连通性测试：GET 一个必然不存在的证书 ID 的状态接口。
    /// 404 = 网络与 ApiKey 均正常；401 = ApiKey 错误；其余 = 不可达/服务异常。
    /// </summary>
    public async Task<CertManagerConnectionResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var (client, _) = await CreateClientAsync(ct);
            var response = await client.GetAsync($"/api/external/certificates/{Guid.NewGuid()}/status", ct);

            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => new CertManagerConnectionResult(true, "连接正常，ApiKey 有效"),
                HttpStatusCode.Unauthorized => new CertManagerConnectionResult(false, "ApiKey 无效（401）"),
                _ => new CertManagerConnectionResult(false, $"服务异常（HTTP {(int)response.StatusCode}）"),
            };
        }
        catch (CertManagerException ex) when (ex.Kind == CertManagerError.NotConfigured)
        {
            return new CertManagerConnectionResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            return new CertManagerConnectionResult(false, $"无法连接 CertManager: {ex.Message}");
        }
    }

    private async Task<(HttpClient Client, string BaseUrl)> CreateClientAsync(CancellationToken ct)
    {
        var settings = await _settings.GetWithApiKeyAsync();
        if (!settings.Enabled)
        {
            throw new CertManagerException("CertManager 接入未启用", CertManagerError.NotConfigured);
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new CertManagerException("CertManager 接入未配置（BaseUrl/ApiKey）", CertManagerError.NotConfigured);
        }

        var client = _httpClientFactory.CreateClient("CertManager");
        client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", settings.ApiKey);
        return (client, settings.BaseUrl);
    }

    private static async Task<ExternalErrorResponse?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ExternalErrorResponse>(JsonOptions, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RequestCertificateResponse(Guid CertificateId, bool Success);

    private sealed record ExternalErrorResponse(bool Success, string? Error);
}
