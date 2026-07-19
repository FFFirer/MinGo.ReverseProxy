# Migration Assemblies

## 多迁移程序集架构

### 概要

同一个 `AppDbContext`（定义在 `MinGo.Infrastructure`）拥有两个独立的迁移程序集，分别在开发环境和生产环境中使用。运行时通过 `ASPNETCORE_ENVIRONMENT` 环境变量选择加载哪个迁移集。

### 约束

- `AppDbContext` 类本身不修改
- 实体配置（EntityConfiguration）不修改
- 数据库 provider 保持 SQLite（不在此 spec 范围内改动）
- 每个迁移程序集必须有其各自的 `IDesignTimeDbContextFactory<AppDbContext>`

### 架构

```text
运行时环境 ─┬─ Development ──→ MigrationsAssembly("MinGo.Infrastructure.DevMigrations")
            │                     └── DevMigrations.dll 中的迁移历史
            │
            └─ Production ────→ MigrationsAssembly("MinGo.Infrastructure.ProdMigrations")
                                  └── ProdMigrations.dll 中的迁移历史
```

#### 场景：开发环境日常迁移

- **GIVEN** 开发者在开发环境工作
- **WHEN** 实体模型发生变更
- **THEN** 使用 `dotnet ef migrations add` 并指定 `--project MinGo.Infrastructure.DevMigrations`
- **AND** 变更只会出现在 DevMigrations 程序集中
- **AND** ProdMigrations 不受影响

#### 场景：开发环境启动迁移

- **GIVEN** ControlPlane.Api 在 Development 模式下启动
- **WHEN** `Database.Migrate()` 被调用
- **THEN** 加载 DevMigrations.dll 中的迁移并执行
- **AND** `__EFMigrationsHistory` 表中记录 DevMigrations namespace 下的 migration 名

#### 场景：生产环境启动迁移

- **GIVEN** ControlPlane.Api 在 Production 或 Staging 模式下启动
- **WHEN** `Database.Migrate()` 被调用
- **THEN** 加载 ProdMigrations.dll 中的迁移并执行
- **AND** 只有经过版本发布的迁移会被应用

#### 场景：发布新版本（增量发布）

- **GIVEN** 一个开发周期结束，实体模型已稳定
- **WHEN** 准备发布新版本
- **THEN** 在 `MinGo.Infrastructure.ProdMigrations` 项目中生成增量迁移 `V{N}_Description`
- **AND** 该迁移仅包含与上一版本 Prod 迁移之间的 diff

#### 场景：开发数据库重建

- **GIVEN** 开发数据库需要重置
- **WHEN** 删除 `mingocp.db` 后重新启动应用
- **THEN** `Database.Migrate()` 从 DevMigrations 的 `InitialCreate` 开始执行全部迁移
- **AND** 数据库被重新创建到最新状态

### 设计时指令格式

```bash
# Dev 迁移（日常开发）
dotnet ef migrations add <MigrationName> \
  --project src/MinGo.Infrastructure.DevMigrations/ \
  --startup-project src/MinGo.ControlPlane.Api/ \
  --output-dir Migrations/

# Prod 迁移（版本发布）
dotnet ef migrations add <V{N}_Description> \
  --project src/MinGo.Infrastructure.ProdMigrations/ \
  --startup-project src/MinGo.ControlPlane.Api/ \
  --output-dir Migrations/
```

### 运行时配置

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
    var env = sp.GetRequiredService<IHostEnvironment>();

    options.UseSqlite(connStr, o =>
    {
        o.MigrationsAssembly(env.IsDevelopment()
            ? "MinGo.Infrastructure.DevMigrations"
            : "MinGo.Infrastructure.ProdMigrations");
    });
});
```
