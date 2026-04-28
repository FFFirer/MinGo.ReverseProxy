# Implementation Tasks: Add Identity Login

> **更正说明**：当前项目为 Blazor Server 架构，应使用 Cookie 认证而非 Bearer Token。

## 1. Add Dependencies

- [x] 1.1 Add `Microsoft.AspNetCore.Identity.EntityFrameworkCore` to `src/MinGo.ReverseProxy/MinGo.ReverseProxy.csproj` - **已在项目中**
- [x] 1.2 Add `Microsoft.identityModel.Protocols.OpenIdConnect` if needed (usually included transitively) - **不需要**

## 2. Configure DbContext

- [x] 2.1 Check existing `ApiDbContext` in `MinGo.Infrastructure` for Identity tables integration
- [x] 2.2 已创建 `ApplicationDbContext` extending `IdentityDbContext<IdentityUser>`
- [x] 2.3 Register `ApplicationDbContext` in Program.cs

## 3. Configure Program.cs（Cookie 方案）

- [x] 3.1 将 `AddIdentityApiEndpoints<IdentityUser>()` 改为 `AddIdentity<IdentityUser>()`
- [x] 3.2 添加 `AddDefaultUI()` 以启用 Identity Razor Pages - **使用现有 Blazor 页面，无需默认 UI**
- [x] 3.3 添加 `AddDefaultTokenProviders()`
- [x] 3.4 配置 Entity Framework stores with `.AddEntityFrameworkStores<ApplicationDbContext>()`
- [x] 3.5 配置 Cookie 策略（HttpOnly, Secure, SameSite）
- [x] 3.6 配置 Data Protection（用于 Cookie 加密）
- [x] 3.7 移除 `MapIdentityApi<IdentityUser>()`（这是 Bearer Token 方案）

## 4. Add Protected Pages

- [x] 4.1 Add protected Razor page requiring authentication
- [x] 4.2 Use `[Authorize]` attribute - **已有 AuthService 实现**

## 5. Configure Identity Options

- [x] 5.1 Configure `PasswordOptions` (minimum length, require uppercase/lowercase/digit)
- [x] 5.2 移除 Bearer Token 相关配置（`BearerTokenOptions`）

## 6. Run Migrations

- [x] 6.1 Run `dotnet ef migrations add AddIdentity` in MinGo.ReverseProxy project
- [x] 6.2 Run `dotnet ef database update` or start app to auto-migrate

## 7. Test (Cookie 方式)

- [ ] 7.1 访问 `/Identity/Account/Register` 注册页面
- [ ] 7.2 提交注册表单（有效凭据）
- [ ] 7.3 提交注册表单（重复邮箱 - 应失败）
- [ ] 7.4 访问 `/Identity/Account/Login` 登录页面
- [ ] 7.5 提交登录表单（有效凭据 - 应设置 Cookie）
- [ ] 7.6 提交登录表单（无效凭据 - 应显示错误）
- [ ] 7.7 访问受保护页面（无 Cookie - 应重定向到登录页）
- [ ] 7.8 访问受保护页面（有 Cookie - 应正常显示）
- [ ] 7.9 点击登出，验证 Cookie 清除