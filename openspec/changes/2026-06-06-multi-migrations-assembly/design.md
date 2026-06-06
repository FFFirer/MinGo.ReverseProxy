## Context

当前 `MinGo.Infrastructure` 项目同时包含 `AppDbContext`（实体配置）和 `Migrations/` 文件夹（迁移历史）。所有环境共用这套迁移。

```text
src/MinGo.Infrastructure/
├── Data/
│   ├── AppDbContext.cs              ← 唯一的 DbContext
│   ├── ApiDbService.cs
│   ├── Configuration/
│   └── AppDbContextDesignTimeFactory.cs
├── Migrations/                      ← 当前 2 个迁移，所有环境共用
│   ├── 20260603003131_InitialCreate.cs
│   ├── 20260605151955_RemoveClusterName.cs
│   └── AppDbContextModelSnapshot.cs
└── MinGo.Infrastructure.csproj
```

目标架构：同一 DbContext，两个迁移程序集，运行时按环境选择。

## Goals / Non-Goals

**Goals:**
- 开发环境的迁移独立于生产环境，互不干扰
- 开发环境可以随意加迁移、重建数据库，不影响生产
- 生产环境迁移增量累加，每个版本一个迁移，与 git tag 绑定
- 运行时通过 `ASPNETCORE_ENVIRONMENT` 自动选择正确的迁移集
- 设计时（`dotnet ef migrations`）通过 `--project` 参数指定目标项目

**Non-Goals:**
- 不修改 `AppDbContext` 类本身
- 不改变实体配置（EntityConfiguration 保持不变）
- 不修改数据库 provider（仍使用 SQLite）
- 不改动业务逻辑层（ApiDbService 等不受影响）
- 不涉及数据库迁移的 CI/CD 自动化（手动 squash）

## Decisions

| 决策 | 选择 | 备选方案 | 理由 |
|------|------|----------|------|
| 迁移项目结构 | **独立类库项目**（DevMigrations + ProdMigrations） | 同一项目不同文件夹 + 条件编译 | 独立项目互不干扰，`dotnet ef` CLI 原生支持 `--project` 指定目标项目，每个项目各自编译为独立 dll |
| Dev 迁移初始化 | **从零重新生成** | 整体迁移现有 2 个迁移 | 旧迁移历史不需要保留，从当前模型直接生成干净的 `InitialCreate`，namespace 自然变为 DevMigrations 的 namespace |
| Prod 迁移策略 | **增量累加**（V1_Initial → V2_AddX → V3_AddY） | 全量替换（每版本删了重来） | 保留生产迁移历史可审计；`dotnet ef migrations add` 在已有迁移基础上自动计算 diff，工作量和全量替换相同 |
| 运行时切换机制 | **`MigrationsAssembly()` 根据 `IHostEnvironment` 条件设置** | 条件编译（#if DEBUG）、配置项驱动 | 环境变量方式与现有 `appsettings.{env}.json` 模式一致，无需额外配置项，测试友好 |
| DesignTimeFactory 位置 | **每个迁移项目各自创建一个 Factory** | 在 Infrastructure 中创建一个统一的 Factory | 各自 Factory 可以针对不同环境配置连接字符串；EF Core CLI 会自动在 `--project` 指定的项目中查找 Factory |
| Infrastructure 中原 Migrations | **物理删除，并在 .csproj 中显式排除** | 仅用 `<Compile Remove>` 排除 | 物理删除更干净，避免开发者误以为 Migrations 还在 Infrastructure 中 |

### 运行时切换代码

```csharp
// src/MinGo.ControlPlane.Api/Program.cs
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
    var env = sp.GetRequiredService<IHostEnvironment>();

    options.UseSqlite(connStr, o =>
    {
        var migrationsAssembly = env.IsDevelopment()
            ? "MinGo.Infrastructure.DevMigrations"
            : "MinGo.Infrastructure.ProdMigrations";

        o.MigrationsAssembly(migrationsAssembly);
    });
});
```

### 设计时指令

```bash
# Dev：日常添加迁移
dotnet ef migrations add AddBillingTable \
  --project src/MinGo.Infrastructure.DevMigrations/ \
  --startup-project src/MinGo.ControlPlane.Api/ \
  --output-dir Migrations/

# Prod：发布时添加版本迁移
dotnet ef migrations add V2_AddPaymentSubscription \
  --project src/MinGo.Infrastructure.ProdMigrations/ \
  --startup-project src/MinGo.ControlPlane.Api/ \
  --output-dir Migrations/
```

### 设计时工厂

```csharp
// DevMigrations/DevDesignTimeFactory.cs
public class DevDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseSqlite("Data Source=DevMigrations.db");
        return new AppDbContext(options.Options);
    }
}

// ProdMigrations/ProdDesignTimeFactory.cs
public class ProdDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connStr = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Production connection string is required");

        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseSqlite(connStr);
        return new AppDbContext(options.Options);
    }
}
```

## Risks / Trade-offs

| 风险 | 缓解措施 |
|------|----------|
| **ModelSnapshot namespace 冲突**：两个项目各自生成 snapshot，都标记 `[DbContext(typeof(AppDbContext))]` | 两个 snapshot 分别编译到不同程序集 dll 中，EF Core 运行时只加载 `MigrationsAssembly` 指定的那个。不存在加载时冲突 |
| **开发数据库无法重用**：现有 `mingocp.db` 的 `__EFMigrationsHistory` 记录的是旧 namespace 的 migration 名，新 DevMigrations 生成的 migration ID 不同 | 开发环境直接删除 `mingocp.db`，应用启动时 `Database.Migrate()` 自动重建。Dev 数据库本就可丢弃 |
| **两个 DesignTimeFactory 的歧义**：`dotnet ef` 如果找到多个 `IDesignTimeDbContextFactory<AppDbContext>` 会报错 | 每个迁移项目独立编译，各自只有一个 Factory。`--project` 指向哪个项目就使用哪个 Factory |
| **ProdMigrations 首次生成 V1_Initial 时的模型状态**：必须确保此时 Dev 的 entity 变更已经完成并经过测试 | 这是发布流程的一部分：在 Dev 阶段开发完成后、打 release tag 前，在 ProdMigrations 中生成 V1 |
| **后续 Prod 迁移的增量生成**：`dotnet ef migrations add V2_AddX` 在已有 V1 的 ProdMigrations 中运行，EF Core 会自动比较 V1 snapshot 和当前模型，生成正确的 diff | 这是 EF Core 的标准行为，无需特殊处理 |
| **开发人员在 DevMigrations 和 Infrastructure 之间混淆迁移位置**：习惯了在 Infrastructure/Migrations 下操作 | 物理删除 Infrastructure/Migrations 文件夹，`dotnet ef` 必须指定 `--project` 到 DevMigrations 才能成功执行，不指定会报错（找不到 DbContext） |
