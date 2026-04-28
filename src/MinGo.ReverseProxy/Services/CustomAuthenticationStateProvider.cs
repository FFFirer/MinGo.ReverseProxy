using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;

namespace MinGo.ReverseProxy.Services;

public class CustomAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IJSRuntime _jsRuntime;
    private readonly NavigationManager _navigationManager;
    private const string AccessTokenKey = "access_token";
    private bool _isInitialized;

    public CustomAuthenticationStateProvider(IJSRuntime jsRuntime, NavigationManager navigationManager)
    {
        _jsRuntime = jsRuntime;
        _navigationManager = navigationManager;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // #PRIORITY: 检测是否在预渲染阶段
        if (_jsRuntime is null)
        {
            // 服务端预渲染 - 返回未认证
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        try
        {
            // 尝试获取 token
            var token = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", AccessTokenKey);
            if (!string.IsNullOrEmpty(token))
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, token),
                    new Claim(ClaimTypes.Authentication, token)
                };
                var identity = new ClaimsIdentity(claims, "Bearer");
                var user = new ClaimsPrincipal(identity);
                return new AuthenticationState(user);
            }
        }
        catch (InvalidOperationException)
        {
            // 预渲染阶段 IJSRuntime 不可用
            // 不跳转，静默返回未认证
        }
        catch
        {
            // 其他异常，返回未认证
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    public void NotifyAuthenticationStateChanged()
    {
        _ = NotifyAuthenticationStateChangedAsync();
    }

    private async Task NotifyAuthenticationStateChangedAsync()
    {
        var state = await GetAuthenticationStateAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(state));
    }
}