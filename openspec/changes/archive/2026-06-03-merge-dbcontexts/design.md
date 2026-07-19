## Context

当前 `ApiDbContext : DbContext` 管理 API 实体，`ApplicationDbContext : IdentityDbContext<IdentityUser>` 管理 Identity。两者使用同一 SQLite 数据库文件，但 `Program.cs` 中分别注册。

`ApplicationDbContext.OnModelCreating()` 中同时调用了 `base.OnModelCreating(builder)`（生成默认 AspNet* 表）和自定义表名配置（生成 Users、Roles 等），导致 `AspNetRoles` 和 `Roles` 两张表同时存在。

## Goals / Non-Goals

**Goals:**
- 单一 `AppDbContext : IdentityDbContext<IdentityUser>` 管理全部表
- 所有表使用自定义名称，无 AspNet* 默认表
- 新旧迁移平滑替换

**Non-Goals:**
- 不改变实体模型
- 不改变数据库物理文件（SQLite）

## Decisions

### 1. 避免继承 base 生成默认 Identity 表

`IdentityDbContext.OnModelCreating()` 会调用 `base.OnModelCreating()` 生成默认的 `AspNetRoles`、`AspNetUsers` 等表。要完全消除默认表名，必须在 `AppDbContext.OnModelCreating()` 中**不调用** `base.OnModelCreating()` 并手动配置所有 Identity 实体。

手动调用 `builder.Entity<IdentityUser>(...)` 并设置 `ToTable("Users")` 等，替代 `base.OnModelCreating()`。

### 2. 迁移策略

删除旧的 `Migrations/` 和 `Migrations/ApplicationDb/`，生成新的初始迁移。由于 SQLite 不支持部分表迁移，生产环境会删除旧库重建。

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| 生产数据库需重建 | SQLite 开发/测试阶段可接受；文档注明需删除旧 db 文件 |
| Identity 表配置遗漏（少配一个实体类型） | 对比旧 ApplicationDbContext 的迁移和 AppDbContext 输出模型，确保完整 |
