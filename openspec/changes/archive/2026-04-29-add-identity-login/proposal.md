# Proposal: Add Identity Login

## Why

MinGo.ReverseProxy 目前没有任何身份验证功能。项目是 **Blazor Server** 架构（ASP.NET Core + Blazor Server 管理面板），缺少用户登录能力会导致：
- 无法保护管理接口安全
- 无法记录操作者身份
- 无法实现基于角色的访问控制

需要为项目添加 ASP.NET Core Identity 登录功能，在安全性、实现复杂度之间取得平衡。

> **更正**：原 proposal 假设前后端分离架构使用 Bearer Token，现改为 Blazor Server 架构使用 Cookie 认证。

## What Changes

- 安装必要的 NuGet 包（如有需要）
- 配置 ASP.NET Core Identity（Cookie 认证方式）
- 添加 User 实体到数据库（使用已有的 EF Core Sqlite）
- 启用 Identity UI 页面（登录、注册）
- 配置 Cookie 中间件
- 添加受保护示例页面（需要登录才能访问）

## Capabilities

### New Capabilities
- **identity-auth**: 用户注册、登录、用户信息管理（Cookie 方式）
  - GET /Identity/Account/Register - 注册页面
  - POST /Identity/Account/Register - 注册提交
  - GET /Identity/Account/Login - 登录页面
  - POST /Identity/Account/Login - 登录提交
  - POST /Identity/Account/Logout - 登出
  - GET /Identity/Account/Manage - 用户信息管理页面

### Modified Capabilities
- 项目已有 `AddIdentityApiEndpoints`，需改为 `AddIdentity` + Cookie

## Impact

- 配置变更：
  - Program.cs 将 `AddIdentityApiEndpoints` 改为 `AddIdentity<IdentityUser>()`
  - 添加 `AddDefaultUI()` 和 `AddDefaultTokenProviders()`
- 依赖：现有的 Microsoft.AspNetCore.Identity.EntityFramework.Core 已足够
- 需要配置 Data Protection（用于 Cookie 加密）
- 不需要 CORS（Blazor Server 同源）