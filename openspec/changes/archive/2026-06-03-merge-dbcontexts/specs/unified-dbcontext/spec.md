## ADDED Requirements

### Requirement: AppDbContext 包含所有实体
系统 SHALL 提供 `AppDbContext`，继承 `IdentityDbContext<IdentityUser>`，包含 API 管理实体（Routes、Clusters、Destinations、Certificates）和 Identity 实体（Users、Roles、Claims、Tokens 等）。

#### Scenario: 所有表使用自定义名称
- **WHEN** 生成迁移脚本
- **THEN** 表名应为 `Users`、`Roles`、`RoleClaims`、`UserClaims`、`UserRoles`、`UserLogins`、`UserTokens`、`ApiRoutes`、`ApiClusters`、`ApiDestinations`、`ApiCertificates`
- **THEN** 不应包含 `AspNetUsers`、`AspNetRoles` 等任何 AspNet* 前缀表

### Requirement: 单一注册点
系统 SHALL 只注册 `AppDbContext` 一个 DbContext，删除 `ApiDbContext` 和 `ApplicationDbContext` 的注册。

#### Scenario: Program.cs 注册
- **WHEN** 应用启动
- **THEN** `builder.Services` 中仅注册 `AppDbContext`
- **THEN** `ApiDbService` 注入 `AppDbContext`

### Requirement: 迁移一致性
数据库迁移 SHALL 统一通过 `AppDbContext` 管理，删除旧的 `Migrations/` 和 `Migrations/ApplicationDb/` 目录。

#### Scenario: 新迁移覆盖所有表
- **WHEN** 执行 `dotnet ef migrations add InitialCreate -c AppDbContext`
- **THEN** 生成的迁移包含所有 API 表和 Identity 表
- **THEN** 无 `AspNetRoles` 等重复表
