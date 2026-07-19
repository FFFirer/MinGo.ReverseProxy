## 1. 新建 DevMigrations 类库项目

- [ ] 1.1 创建 `src/MinGo.Infrastructure.DevMigrations/MinGo.Infrastructure.DevMigrations.csproj`
  - 目标框架 `net10.0`，引用 `MinGo.Infrastructure`
  - 添加 `Microsoft.EntityFrameworkCore.Design` 包引用（私有资产）
  - 不含 `Microsoft.EntityFrameworkCore.Sqlite`（由 Infrastructure 传递引用）
- [ ] 1.2 在 `MinGo.ReverseProxy.slnx` 中添加新项目引用

## 2. 新建 ProdMigrations 类库项目

- [ ] 2.1 创建 `src/MinGo.Infrastructure.ProdMigrations/MinGo.Infrastructure.ProdMigrations.csproj`
  - 目标框架 `net10.0`，引用 `MinGo.Infrastructure`
  - 添加 `Microsoft.EntityFrameworkCore.Design` 包引用（私有资产）
- [ ] 2.2 在 `MinGo.ReverseProxy.slnx` 中添加新项目引用

## 3. 添加设计时工厂

- [ ] 3.1 在 DevMigrations 中创建 `DevDesignTimeFactory.cs`
  - 实现 `IDesignTimeDbContextFactory<AppDbContext>`
  - 连接字符串 `"Data Source=DevMigrations.db"`（开发用 SQLite）
- [ ] 3.2 在 ProdMigrations 中创建 `ProdDesignTimeFactory.cs`
  - 实现 `IDesignTimeDbContextFactory<AppDbContext>`
  - 从 `appsettings.json` + 环境变量读取连接字符串
  - 找不到连接字符串时抛出明确异常
- [ ] 3.3 验证两个 factory 在不同 project 下互不冲突

## 4. 移除 Infrastructure 中的 Migrations 文件夹

- [ ] 4.1 物理删除 `src/MinGo.Infrastructure/Migrations/` 目录
  - 删除 3 个文件：`InitialCreate.cs`、`RemoveClusterName.cs`、`AppDbContextModelSnapshot.cs`
- [ ] 4.2 清理 `MinGo.Infrastructure.csproj`：确保无残留的 Migrations 引用
  - 可选：添加 `<Compile Remove="Migrations/**/*.cs" />` 作为安全网

## 5. 在 DevMigrations 中重新生成初始迁移

- [ ] 5.1 删除开发环境的旧数据库 `mingocp.db`
- [ ] 5.2 运行 `dotnet ef migrations add InitialCreate --project src/MinGo.Infrastructure.DevMigrations/ --startup-project src/MinGo.ControlPlane.Api/ --output-dir Migrations/`
- [ ] 5.3 验证生成的迁移包含所有实体表（Routes, Clusters, Destinations, Certificates + Identity 表）

## 6. 修改 ControlPlane.Api 运行时配置

- [ ] 6.1 在 `MinGo.ControlPlane.Api.csproj` 中添加对 DevMigrations 和 ProdMigrations 的 ProjectReference
- [ ] 6.2 修改 `Program.cs` 中的 `AddDbContext`：
  - 注入 `IServiceProvider` 获取 `IHostEnvironment`
  - 开发环境 → `MigrationsAssembly("MinGo.Infrastructure.DevMigrations")`
  - 非开发环境 → `MigrationsAssembly("MinGo.Infrastructure.ProdMigrations")`
- [ ] 6.3 `dotnet build` 验证编译通过

## 7. 验证 Dev 环境迁移生效

- [ ] 7.1 运行 `ControlPlane.Api`，确认 `Database.Migrate()` 成功创建所有表
- [ ] 7.2 检查 `__EFMigrationsHistory` 表中记录的是 DevMigrations namespace 的 migration 名称

## 8. 首次发布时生成 Prod 迁移（手动步骤）

- [ ] 8.1 确认当前模型已稳定
- [ ] 8.2 运行 `dotnet ef migrations add V1_Initial --project src/MinGo.Infrastructure.ProdMigrations/ --startup-project src/MinGo.ControlPlane.Api/ --output-dir Migrations/`
- [ ] 8.3 验证 `V1_Initial.cs` 包含所有表结构的 CreateTable
- [ ] 8.4 commit + tag `v1.0.0`
