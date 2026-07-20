## Why

当前生产环境中，`GET /api/instances/{id}/config` 接口始终返回 `500 {"message":"TrySendEventAsync not configured"}`。这是因为 `InstanceConfigQueryService.TrySendEventAsync` 回调只在开发环境（`IsDevelopment()`）中被装配，生产环境中该回调保持为 null，导致配置查询功能完全不可用。

## What Changes

- 将 `Program.cs` 中 `TrySendEventAsync` 回调的装配代码从 `if (app.Environment.IsDevelopment())` 块中移出，使其在所有环境（开发/生产）中无条件执行
- 开发环境块仅保留数据库迁移和种子数据逻辑

## Capabilities

### New Capabilities

- `instance-config-query`: 通过 API 查询数据面实例的当前 YARP 运行时配置（Routes + Clusters）

### Modified Capabilities

- （无，纯实现修复，不涉及 spec 级别的行为变更）

## Impact

- `src/MinGo.ControlPlane.Api/Program.cs`：回调装配位置调整
- 生产环境部署后 `GET /api/instances/{id}/config` 可正常返回实例配置
