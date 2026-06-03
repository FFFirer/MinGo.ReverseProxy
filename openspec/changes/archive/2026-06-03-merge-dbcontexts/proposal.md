## Why

当前项目维护两个独立的 EF Core DbContext（`ApiDbContext` 和 `ApplicationDbContext`），它们操作同一个 SQLite 数据库，但各自维护独立的迁移集。这导致：

- 迁移管理复杂 — 需要分别生成和应用两个 DbContext 的迁移
- `efbundle` 只能针对一个 DbContext，容易遗漏（此前 ApplicationDbContext 未被迁移导致"未登录"错误）
- `Program.cs` 需要分别注册两个 DbContext，`ApiDbService` 直接耦合 `ApiDbContext`
- Identity 表的迁移中同时生成了 `AspNetRoles`（默认名）和 `Roles`（自定义名）两个表

## What Changes

- 创建 `AppDbContext`，继承 `IdentityDbContext<IdentityUser>`，合并 API 实体和 Identity 表
- 确保所有表使用自定义名称（`Users`、`Roles`、`RoleClaims`、`UserClaims`、`UserRoles`、`UserLogins`、`UserTokens`），不再生成 `AspNetRoles` 等默认表名
- 删除 `ApiDbContext`、`ApplicationDbContext`、`ApiDbContextDesignTimeFactory`
- 更新 `ApiDbService` → 注入 `AppDbContext`
- 更新 `Program.cs` → 只注册 `AppDbContext`
- 更新 `ControlPlane.Dockerfile` → efbundle 使用 `-c AppDbContext`
- 删除旧迁移文件夹，生成新的统一初始迁移

## Capabilities

### New Capabilities
- `unified-dbcontext`: 统一的 AppDbContext，合并 API 管理实体和 ASP.NET Core Identity 表

### Modified Capabilities

（无）

## Impact

- `MinGo.Infrastructure.Data`: 增加 AppDbContext，删除 ApiDbContext、ApplicationDbContext
- `MinGo.ControlPlane.Api`: Program.cs 注册变更
- `ControlPlane.Dockerfile`: efbundle 目标变更
- 数据库：新迁移将创建所有表（仅自定义表名，无 AspNet* 前缀表）
