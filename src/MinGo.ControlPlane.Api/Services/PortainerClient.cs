using System.Net;
using System.Text.Json;
using MinGo.ControlPlane.Api.Models.Portainer;
using MinGo.Core.Entities;

namespace MinGo.ControlPlane.Api.Services;

/// <summary>
/// Portainer 只读 API 客户端（system/status、endpoints、containers、networks）。
/// 由 PortainerClientFactory 按实例创建，不再直接绑定 DI Options。
/// </summary>
public sealed class PortainerClient
{
    private const string ApiKeyHeader = "X-API-KEY";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly PortainerInstanceEntity _instance;
    private readonly ILogger<PortainerClient> _logger;

    internal PortainerClient(
        HttpClient http,
        PortainerInstanceEntity instance,
        ILogger<PortainerClient> logger)
    {
        _http = http;
        _instance = instance;
        _logger = logger;

        if (TryGetBaseUri(instance.BaseUrl) is { } baseUri)
        {
            _http.BaseAddress = EnsureTrailingSlash(baseUri);
        }

        if (!string.IsNullOrWhiteSpace(instance.ApiKey))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeader, instance.ApiKey.Trim());
        }

        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(instance.TimeoutSeconds, 1, 120));
    }

    public string InstanceId => _instance.Id;

    public bool IsConfigured =>
        _instance.Enabled &&
        !string.IsNullOrWhiteSpace(_instance.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_instance.ApiKey) &&
        _http.BaseAddress is not null;

    public string? NotConfiguredReason
    {
        get
        {
            if (!_instance.Enabled)
            {
                return $"Portainer 实例 '{_instance.Id}' 未启用";
            }

            if (string.IsNullOrWhiteSpace(_instance.BaseUrl))
            {
                return $"Portainer 实例 '{_instance.Id}' 未配置 BaseUrl";
            }

            if (string.IsNullOrWhiteSpace(_instance.ApiKey))
            {
                return $"Portainer 实例 '{_instance.Id}' 未配置 ApiKey";
            }

            if (_http.BaseAddress is null)
            {
                return $"Portainer 实例 '{_instance.Id}' 的 BaseUrl 不是合法的绝对 URL";
            }

            return null;
        }
    }

    public Task<PortainerSystemStatusDto> GetStatusAsync(CancellationToken ct) =>
        GetJsonAsync<PortainerSystemStatusDto>("api/system/status", ct);

    public async Task<IReadOnlyList<PortainerEndpointDto>> GetEndpointsAsync(CancellationToken ct)
    {
        var endpoints = await GetJsonAsync<List<PortainerEndpointDto>>("api/endpoints", ct);
        return endpoints;
    }

    public async Task<IReadOnlyList<PortainerContainerDto>> GetContainersAsync(int endpointId, CancellationToken ct)
    {
        var containers = await GetJsonAsync<List<PortainerContainerDto>>(
            $"api/endpoints/{endpointId}/docker/containers/json?all=1", ct);
        return containers;
    }

    public async Task<IReadOnlyList<PortainerNetworkDto>> GetNetworksAsync(int endpointId, CancellationToken ct)
    {
        var networks = await GetJsonAsync<List<PortainerNetworkDto>>(
            $"api/endpoints/{endpointId}/docker/networks", ct);
        return networks;
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        string body;
        try
        {
            using var response = await _http.GetAsync(path, ct);
            body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var message = DescribeHttpError(response.StatusCode, body);
                _logger.LogWarning("Portainer [{InstanceId}] 请求 {Path} 失败：{Status} {Message}",
                    _instance.Id, path, (int)response.StatusCode, message);
                throw new PortainerException(message, (int)response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new PortainerException($"无法连接 Portainer [{_instance.Id}]：{ex.Message}", 502, ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PortainerException(
                $"Portainer [{_instance.Id}] 请求超时（{_http.Timeout.TotalSeconds:0} 秒）", 504, ex);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new PortainerException($"Portainer [{_instance.Id}] 返回空响应：{path}", 502);
        }
        catch (JsonException ex)
        {
            throw new PortainerException($"Portainer [{_instance.Id}] 响应解析失败：{path}", 502, ex);
        }
    }

    private static Uri? TryGetBaseUri(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        return Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ? uri : null;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.ToString();
        return value.EndsWith('/') ? uri : new Uri(value + '/', UriKind.Absolute);
    }

    private static string DescribeHttpError(HttpStatusCode status, string body)
    {
        var detail = TryExtractMessage(body);
        var summary = status switch
        {
            HttpStatusCode.Unauthorized => "Portainer 拒绝访问（401），请检查 ApiKey",
            HttpStatusCode.Forbidden => "当前 ApiKey 无权访问该资源（403）",
            HttpStatusCode.NotFound => "Portainer 端点不存在（404），请确认 BaseUrl 与版本",
            HttpStatusCode.TooManyRequests => "Portainer 请求过于频繁（429）",
            _ when (int)status >= 500 => $"Portainer 服务端错误（{(int)status}）",
            _ => $"Portainer 请求失败（{(int)status}）"
        };

        return detail is null ? summary : $"{summary}：{detail}";
    }

    private static string? TryExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var property in new[] { "message", "details", "detail", "title" })
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty(property, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    var message = value.GetString();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return message.Length > 300 ? message[..300] : message;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // 非 JSON 错误页：截断原文。
        }

        var trimmed = body.Trim();
        return trimmed.Length == 0 ? null : trimmed.Length > 300 ? trimmed[..300] : trimmed;
    }
}
