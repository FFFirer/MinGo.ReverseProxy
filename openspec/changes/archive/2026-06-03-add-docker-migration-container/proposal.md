## Why

控制面容器在 Production 环境下启动时，EF Core 数据库迁移不会自动执行（`Program.cs` 中迁移仅在 `IsDevelopment()` 时运行），导致 Routes、Clusters、Certificates 表不存在。当数据面通过 gRPC 连接控制面请求配置同步时，服务端 `BuildConfigSnapshotAsync()` 查询数据库抛出异常，返回 `"Exception was thrown by handler"`，容器运行报错。

## What Changes

- 在 `docker-compose.yml` 中新增 `migrate` 一次性服务，在 `control-plane` 启动前运行 EF Core 迁移（efbundle）
- `control-plane` 添加 `depends_on` 条件，等待 `migrate` 容器成功完成后再启动
- 无需修改 `ControlPlane.Dockerfile`（已包含 efbundle 构建步骤）
- 无需修改 `Program.cs`（迁移交由独立容器负责）

## Capabilities

### New Capabilities
- `docker-db-migration`: 通过 Docker Compose 中的一次性容器自动执行 EF Core 数据库迁移，确保控制面启动时数据库 schema 已就绪

### Modified Capabilities

（无已有 spec，无需修改）

## Impact

- `docker-compose.yml`: 新增 `migrate` 服务定义，修改 `control-plane` 的 `depends_on`
- `ControlPlane.Dockerfile`: 无需修改（已有 efbundle 构建）
- `cp-data` 卷: 迁移容器和 control-plane 共享同一持久卷
- 无代码变更，仅基础设施编排变更
