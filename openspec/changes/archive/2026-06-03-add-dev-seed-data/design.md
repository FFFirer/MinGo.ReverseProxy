## Context

ASP.NET Core Identity + SQLite，启动时自动迁移但无种子数据。每次重建数据库后都需要手动注册才能登录。

已有基础设施：
- `Program.cs` 中 `if (app.Environment.IsDevelopment())` 代码块
- `AddUserSecrets<Program>()` 已配置
- Identity 使用 `IdentityUser`，密码最少 4 位

## Goals / Non-Goals

**Goals:**
- 开发环境首次启动时自动创建默认管理员
- 幂等——数据库已有用户时跳过
- 管理员凭据可通过配置覆盖

**Non-Goals:**
- 不影响生产环境（只限 Development）
- 不引入新的依赖
- 不修改 AuthController 或 Identity 配置

## Decisions

### 1. 独立的 DbInitializer 类
将种子逻辑抽取到 `Data/DbInitializer.cs`，而非写在 `Program.cs` 里。

优点：职责单一、可单独测试、`Program.cs` 不膨胀。

### 2. 凭据来源：代码默认值 + 配置覆盖

```
代码默认值:
  AdminEmail    = "admin@mingo.local"
  AdminPassword = "admin123"

配置覆盖 (appsettings.Development.json):
  "SeedData": {
    "AdminEmail": "admin@mingo.local",
    "AdminPassword": "admin123"
  }
```

`IConfiguration.GetSection("SeedData")` 读取，代码内提供 fallback 默认值。如果配置里没有对应节，用代码默认值。

### 3. 幂等判定：检查是否有任何用户

```csharp
if (!await userManager.Users.AnyAsync())
{
    // 创建默认管理员
}
```

而不是检查特定邮箱——防止用户删除了 admin 后又重建的冲突情况。

### 4. 放置位置

```
src/MinGo.ControlPlane.Api/
  └── Data/
       └── DbInitializer.cs
```

## Risks / Trade-offs

- 无显著风险。种子逻辑只在 Development 下执行，且幂等。
- `admin123` 在代码里可见，但开发环境默认密码在代码中硬编码是常见做法（如 Grafana 默认 admin/admin）。生产环境不执行此逻辑。
