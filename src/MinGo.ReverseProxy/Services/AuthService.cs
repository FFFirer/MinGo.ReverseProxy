using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace MinGo.ReverseProxy.Services;

/// <summary>
/// 认证服务 - 使用 ASP.NET Core Identity Cookie 认证
/// 适用于 Blazor Server，通过 SignInManager 直接管理 Cookie
/// </summary>
public class AuthService
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly AuthenticationStateProvider _authStateProvider;

    public AuthService(
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager,
        AuthenticationStateProvider authStateProvider)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _authStateProvider = authStateProvider;
    }

    /// <summary>
    /// 使用邮箱和密码登录，通过 Cookie 建立认证会话
    /// </summary>
    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return AuthResult.Fail("用户不存在");
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName ?? email,
            password,
            isPersistent: true,
            lockoutOnFailure: false);

        if (result.Succeeded)
            return AuthResult.Ok();

        if (result.IsLockedOut)
            return AuthResult.Fail("账户已被锁定，请稍后再试");

        if (result.RequiresTwoFactor)
            return AuthResult.Fail("需要两步验证");

        return AuthResult.Fail("邮箱或密码错误");
    }

    /// <summary>
    /// 注册新用户
    /// </summary>
    public async Task<AuthResult> RegisterAsync(string email, string password)
    {
        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, password);

        if (result.Succeeded)
            return AuthResult.Ok();

        var errors = string.Join("; ", result.Errors.Select(e => e.Description));
        return AuthResult.Fail(errors);
    }

    /// <summary>
    /// 登出，清除 Cookie
    /// </summary>
    public async Task LogoutAsync()
    {
        await _signInManager.SignOutAsync();
    }

    /// <summary>
    /// 检查当前用户是否已认证（基于 Cookie）
    /// </summary>
    public async Task<bool> IsAuthenticatedAsync()
    {
        var state = await _authStateProvider.GetAuthenticationStateAsync();
        return state.User.Identity?.IsAuthenticated ?? false;
    }

    /// <summary>
    /// 获取当前登录用户信息
    /// </summary>
    public async Task<UserInfoDto?> GetUserInfoAsync()
    {
        var state = await _authStateProvider.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated != true)
            return null;

        var identityUser = await _userManager.GetUserAsync(state.User);
        if (identityUser == null)
            return null;

        return new UserInfoDto
        {
            Id = identityUser.Id,
            Email = identityUser.Email ?? "",
            UserName = identityUser.UserName,
            PhoneNumber = identityUser.PhoneNumber,
            EmailConfirmed = identityUser.EmailConfirmed
        };
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

public class AuthResult
{
    public bool Succeeded { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;

    public static AuthResult Ok() => new() { Succeeded = true };
    public static AuthResult Fail(string error) => new() { Succeeded = false, ErrorMessage = error };
}
