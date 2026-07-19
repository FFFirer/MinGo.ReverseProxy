## Why

当前 `AppDbContext` 的所有 EF Core 迁移文件都集中在 `MinGo.Infrastructure/Migrations/` 下，开发环境和生产环境共用同一套迁移历史。这导致两个问题：

1. **开发环境迁移膨胀**：频繁修改实体 → 频繁 `migrations add` → Migrations 文件夹迅速膨胀。开发环境的数据库可以随时 drop/recreate，不需要保留完整的迁移历史。
2. **生产环境风险**：所有开发中的中间状态迁移（包括后来回滚的、改了一半的）都出现在生产迁移历史中。生产迁移应该只包含经过测试、与版本绑定的稳定变更。

## What Changes

1. 新建 `MinGo.Infrastructure.DevMigrations` 类库项目，存放开发环境的迁移文件
2. 新建 `MinGo.Infrastructure.ProdMigrations` 类库项目，存放生产环境的迁移文件
3. 移除 `MinGo.Infrastructure` 项目中的 `Migrations/` 文件夹（DbContext 只保留实体配置）
4. 在 DevMigrations 中从零重新生成初始迁移，不再保留旧的迁移历史
5. 在 `ControlPlane.Api/Program.cs` 中运行时根据环境变量选择 MigrationsAssembly
6. 为两个迁移项目各创建一个 `IDesignTimeDbContextFactory`
7. 首次发布时在 ProdMigrations 中生成 `V1_Initial` 迁移

## Capabilities

### New Capabilities

- `migration-assemblies`: 同一 DbContext 对应多个迁移程序集，通过运行时环境选择加载哪个迁移集。Dev 使用 DevMigrations（频繁变更、可丢弃），Prod 使用 ProdMigrations（增量累加、与版本绑定）。

### Modified Capabilities

> 无现有 spec 需要修改。

## Impact

- **新增项目（2 个类库）**:
  - `src/MinGo.Infrastructure.DevMigrations/MinGo.Infrastructure.DevMigrations.csproj`
  - `src/MinGo.Infrastructure.ProdMigrations/MinGo.Infrastructure.ProdMigrations.csproj`
- **修改文件**:
  - `src/MinGo.Infrastructure/MinGo.Infrastructure.csproj`（排除 Migrations 文件夹）
  - `src/MinGo.ControlPlane.Api/Program.cs`（运行时 MigrationsAssembly 切换）
  - `src/MinGo.ControlPlane.Api/MinGo.ControlPlane.Api.csproj`（新增 ProjectReference）
- **新建文件（4 个）**:
  - `DevMigrations/DevDesignTimeFactory.cs`
  - `DevMigrations/Migrations/InitialCreate.cs`（重新生成）
  - `ProdMigrations/ProdDesignTimeFactory.cs`
  - `ProdMigrations/Migrations/V1_Initial.cs`（首次发布时生成）
- **删除**: `MinGo.Infrastructure/Migrations/` 整个文件夹（含 2 个迁移 + snapshot）
- **数据库变更**: 开发环境的 `mingocp.db` 需删除重建（迁移 namespace 变更）
