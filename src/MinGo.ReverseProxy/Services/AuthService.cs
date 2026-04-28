using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MinGo.ReverseProxy.Services;

public class AuthService
{
    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly NavigationManager _navigationManager;
    private const string AccessTokenKey = "access_token";
    private const string RefreshTokenKey = "refresh_token";

    public AuthService(HttpClient httpClient, IJSRuntime jsRuntime, NavigationManager navigationManager)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _navigationManager = navigationManager;
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var token = await GetAccessTokenAsync();
        return !string.IsNullOrEmpty(token);
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", AccessTokenKey);
    }

    public async Task<string?> GetRefreshTokenAsync()
    {
        return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", RefreshTokenKey);
    }

    public async Task LoginAsync(string email, string password)
    {
        var loginData = new
        {
            email = email,
            password = password,
            useCookies = false
        };

        var response = await _httpClient.PostAsJsonAsync("api/account/login", loginData);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (result != null)
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", AccessTokenKey, result.AccessToken);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", RefreshTokenKey, result.RefreshToken);
        }
    }

    public async Task RegisterAsync(string email, string password)
    {
        var registerData = new
        {
            email = email,
            password = password
        };

        var response = await _httpClient.PostAsJsonAsync("api/account/register", registerData);
        response.EnsureSuccessStatusCode();
    }

    public async Task LogoutAsync()
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", AccessTokenKey);
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
    }

    public async Task<bool> RefreshTokenAsync()
    {
        var refreshToken = await GetRefreshTokenAsync();
        if (string.IsNullOrEmpty(refreshToken))
            return false;

        var refreshData = new { refresh_token = refreshToken };
        var response = await _httpClient.PostAsJsonAsync("api/account/refresh", refreshData);
        
        if (!response.IsSuccessStatusCode)
            return false;

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (result == null)
            return false;

        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", AccessTokenKey, result.AccessToken);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", RefreshTokenKey, result.RefreshToken);
        
        return true;
    }

    public async Task<UserInfoDto?> GetUserInfoAsync()
    {
        var token = await GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            var response = await _httpClient.GetAsync("api/account/me");
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<UserInfoDto>();
        }
        catch
        {
            return null;
        }
    }

    private class LoginResponse
    {
        [JsonPropertyName("token")]
        public string AccessToken { get; set; } = "";
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }
    }

    public class UserInfoDto
    {
        public string Id { get; set; } = "";
        public string Email { get; set; } = "";
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }
        public bool EmailConfirmed { get; set; }
    }
}