# Design: Add Identity Login

## Context

当前 MinGo.ReverseProxy 项目没有任何身份验证功能。项目为 **Blazor Server** 架构（非前后端分离）：
- 后端：ASP.NET Core 10（使用 EF Core Sqlite, Vite.AspNetCore）
- 前端：Blazor Server + Razor Pages（通过 Vite 开发服务器提供 HMR）
- 认证方式：Cookie 认证（适合 Blazor Server）
- 目前所有接口均无保护

项目使用 SQLite 数据库，已有 ApiDbContext。项目目标框架是 net10.0。

> **更正说明**：原设计假设为前后端分离 SPA 架构，使用 Bearer Token。经确认为 Blazor Server 项目，应使用 Cookie 认证。

## Goals / Non-Goals

**Goals:**
- 实现用户注册、登录功能
- 使用 **Cookie 认证**（Blazor Server 友好）
- 集成到现有 EF Core Sqlite 数据库
- 最小化实现复杂度

**Non-Goals:**
- 不实现外部登录（Google, GitHub 等）
- 不实现复杂的角色/权限系统（预留但不实现）
- 不使用 IdentityServer（复杂度太高）
- 不使用 Bearer Token（不适合 Blazor Server）

## Decisions

### 1. 认证方案：Cookie 认证 + Identity UI

选择使用 ASP.NET Core Identity 标准方案 + Cookie 认证：

**替代方案对比：**

| 方案 | 适用场景 | 代码量 | 复杂度 |
|------|----------|--------|--------|
| Identity + Cookie | Blazor Server/Razor Pages ✅ | ~20行 | 最低 |
| Identity API Endpoints + Bearer | SPA/API 项目 | ~15行 | 最低 |
| 手动 JWT | API 项目 | ~80行 | 中等 |

**理由：**
- Blazor Server 运行在服务器端，天然适合 Cookie 认证
- 无需前端存储 Token，安全性更高
- 与 Blazor 授权无缝集成 `[Authorize]` 特性即可
- 原项目已配置 `AddIdentityApiEndpoints`，需调整为 Cookie

### 2. 用户存储：复用现有 Sqlite

使用已有的 ApplicationDbContext：

```csharp
// 已在 MinGo.Infrastructure 中配置
public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    // 继承 IdentityDbContext 已包含 Users, Roles, UserLogins 等
}
```

**理由：**
- 避免新增数据库
- 复用现有迁移机制
- 与现有项目结构一致

### 3. 认证方式：Cookie（不是 Bearer Token）

**关键配置变更：**
- 移除 `AddIdentityApiEndpoints`（这是为 SPA 设计的）
- 改用 `AddIdentity<IdentityUser>` + `AddDefaultUI()`
- 启用 Cookie 中间件

**登录流程：**
1. 用户访问 `/Identity/Account/Login` 登录页面
2. 提交表单到 `/Identity/Account/Login`（POST）
3. 服务器验证后设置 Cookie
4. 后续请求自动携带 Cookie

**理由：**
- Blazor Server 页面渲染在服务端，Cookie 自动传递
- 不需要前端额外处理 Token 存储
- `[Authorize]` 属性直接支持

### 4. 数据保护配置

需要配置 Data Protection 用于 Cookie 加密：

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(env.ContentRootPath, "..", "keys")));
```

**理由：**
- Cookie 需要加密/签名
- 多实例部署时需要共享密钥存储
- 生产环境应使用 Azure Key Vault 或 Redis

## Risks / Trade-offs

### [Risk] 多实例部署时 Cookie 不兼容
→ **Mitigation**: 配置共享 Data Protection 存储（Redis/数据库/文件共享）

### [Risk] 密码强度验证默认较弱
→ **Mitigation**: 配置 PasswordOptions 增强要求（至少 8 位，含大小写数字）

### [Risk] Cookie 容易被 XSS 窃取
→ **Mitigation**: 配置 `CookieHttpOnly = true`, `CookieSecurePolicy = Secure`

### [Trade-off] Cookie vs Bearer Token
- **Cookie 方案**：适合 Blazor Server，简单安全
- **Bearer Token**：适合纯 API/SPA，需要前端额外处理

当前项目是 Blazor Server，Cookie 是正确选择。

## 实施步骤

1. 修改 Program.cs 将 `AddIdentityApiEndpoints` 改为 `AddIdentity` + `AddDefaultUI`
2. 添加 Razor Pages 布局和视图支持
3. 确保 ApplicationDbContext 继承 `IdentityDbContext<IdentityUser>`
4. 配置 Cookie 策略（HttpOnly, Secure, SameSite）
5. 添加受保护示例页面（需要登录才能访问）
6. 运行迁移
7. 测试注册/登录流程