## Context

当前控制面容器（`control-plane`）启动时，EF Core 数据库迁移仅在 `Development` 环境下执行：

```csharp
// ControlPlane.Api/Program.cs:99-104
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.Migrate();
}
```

在 Docker 容器中 `ASPNETCORE_ENVIRONMENT=Production`，迁移不会执行。但 `ControlPlane.Dockerfile` 已通过 `dotnet ef migrations bundle` 构建了自包含的迁移可执行文件（`efbundle`），存放在运行时镜像的 `/app/efbundle` 中，只是从未被调用。

## Goals / Non-Goals

**Goals:**
- 在 docker-compose 中加入一次性迁移容器，在控制面启动前完成数据库 schema 创建
- 复用现有 `ControlPlane.Dockerfile` 构建产物（efbundle）
- 保证迁移容器与 control-plane 使用同一持久化数据卷（`cp-data`）
- `control-plane` 容器等待迁移成功后启动（`depends_on: condition: service_completed_successfully`）

**Non-Goals:**
- 不修改 `ControlPlane.Dockerfile`（现有多阶段构建已包含 efbundle）
- 不修改 `Program.cs` 的迁移逻辑（迁移容器方案更符合容器化最佳实践：Init Container 模式）
- 不处理数据面容器问题（端口 80 冲突是独立问题）

## Decisions

### 1. 复用 ControlPlane.Dockerfile 而非新建专用 Dockerfile

| 选项 | 说明 | 结论 |
|------|------|------|
| 复用现有 Dockerfile + 覆盖 entrypoint | 同一镜像，`entrypoint: ["./efbundle"]` 来运行迁移 | ✅ **选择** |
| 新建 Migration.Dockerfile | 从 SDK 镜像单独构建 | ❌ 重复构建，增加维护成本 |

**原因**: `ControlPlane.Dockerfile` 的运行时阶段已包含 `/app/efbundle`（位于 WORKDIR `/app`），直接设置 `entrypoint: ["./efbundle"]` 即可运行。无需新增 Dockerfile 或构建步骤。

### 2. 使用 `entrypoint` 覆盖而非 `command`

Docker 的 `ENTRYPOINT` + `CMD` 组合规则：
- `ENTRYPOINT ["dotnet", "MinGo.ControlPlane.Api.dll"]`（Dockerfile 定义）
- `command: ["./efbundle"]` → 实际执行 `dotnet MinGo.ControlPlane.Api.dll ./efbundle` ❌

因此必须显式覆盖 `entrypoint`：
- `entrypoint: ["./efbundle"]` → 直接执行 efbundle ✅

### 3. 迁移容器网络和端口

迁移容器只需访问 SQLite 数据库文件（通过卷挂载），不需要网络监听。因此：
- 不映射任何端口
- 不需要加入控制面网络（默认 bridge 即可）
- `restart: "no"` — 一次性任务

### 4. 幂等性

`dotnet ef migrations bundle` 生成的 efbundle 会自动检测已应用的迁移（通过 `__EFMigrationsHistory` 表），仅应用未执行的迁移。多次运行安全。

## Risks / Trade-offs

| 风险 | 缓解措施 |
|------|---------|
| efbundle 执行失败导致 control-plane 无法启动 | `depends_on: condition: service_completed_successfully` 确保 control-plane 不会在失败时启动，符合 Fail-Fast 原则 |
| 迁移容器名称冲突 | 使用唯一名称 `min-go-migrate` |
| efbundle 版本与代码不同步（构建缓存） | 与 control-plane 使用同一 Dockerfile 构建，同时重建 |
| 迁移容器完成后需要清理 | `restart: "no"` 确保容器退出后不会重启，可安全保留日志用于调试 |
