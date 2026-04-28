using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.JSInterop;

namespace MinGo.ReverseProxy.Services;

/// <summary>
/// 自动将 Bearer Token 附加到请求头的 HttpHandler
/// </summary>
public class AuthMessageHandler : DelegatingHandler
{
    private readonly IJSRuntime _jsRuntime;
    private const string AccessTokenKey = "access_token";

    public AuthMessageHandler(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var token = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", AccessTokenKey);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }
        catch
        {
            // 预渲染阶段 IJSRuntime 不可用，跳过 token 设置
        }

        var response = await base.SendAsync(request, cancellationToken);

        // 如果是 401，尝试刷新 token 后重试
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var refreshed = await TryRefreshTokenAsync();
            if (refreshed)
            {
                // 重新获取 token 并重试请求
                try
                {
                    var newToken = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", AccessTokenKey);
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                        response = await base.SendAsync(request, cancellationToken);
                    }
                }
                catch
                {
                    // 忽略重试错误
                }
            }
        }

        return response;
    }

    private async Task<bool> TryRefreshTokenAsync()
    {
        try
        {
            var refreshToken = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
                return false;

            // 调用刷新端点
            var httpClient = new HttpClient();
            var refreshData = new { refreshToken = refreshToken };
            var content = new StringContent(
                JsonSerializer.Serialize(refreshData),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await httpClient.PostAsync("api/account/refresh", content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                if (result != null)
                {
                    await _jsRuntime.InvokeVoidAsync("localStorage.setItem", AccessTokenKey, result.AccessToken);
                    await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "refresh_token", result.RefreshToken);
                    return true;
                }
            }
        }
        catch
        {
            // 忽略刷新错误
        }

        return false;
    }

    private class LoginResponse
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }
}