## Why

项目没有任何种子数据，首次启动后数据库为空，用户需要手动注册才能使用。在开发环境中，每次重置数据库后都需要重新注册，效率低下。需要一个自动化的种子数据机制，在首次运行时创建默认管理员账户。

## What Changes

1. 新增 `DbInitializer.cs` — 独立的种子数据类，幂等地创建默认管理员
2. 修改 `Program.cs` — 在 Development 初始化块中调用种子逻辑
3. 新增配置项 — 管理员邮箱和密码可在 `appsettings.Development.json` 中覆盖

## Capabilities

### New Capabilities

### Modified Capabilities

## Impact

- `src/MinGo.ControlPlane.Api/Data/DbInitializer.cs` — 新增文件
- `src/MinGo.ControlPlane.Api/Program.cs` — 在 `IsDevelopment` 块中添加种子调用
- `src/MinGo.ControlPlane.Api/appsettings.Development.json` — 添加 `SeedData` 配置节
