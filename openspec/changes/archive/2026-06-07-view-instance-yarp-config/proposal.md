## Why

数据面（DataPlane）实例当前没有对外暴露其实际生效的 YARP 配置。控制面推送给数据面的配置与数据面实际运行的配置可能存在不一致（例如 gRPC 推送失败、配置转换差异等）。运维人员无法验证数据面实例"实际在用的配置是什么"，导致排障困难。

## What Changes

1. **新增 `config-query` 事件类型**：在 `dataplane.proto` 的 `EventSubscription` 中增加 `CONFIG_QUERY` 和 `CONFIG_REPORT` 事件类型，控制面通过已有 gRPC 双向流请求数据面当前配置，数据面通过 `DataPlaneConfigProvider.GetConfig()` 获取实时配置并上报。
2. **数据面新增事件处理器**：DataPlane 的 `EventSubscription` 客户端处理 `CONFIG_QUERY` 事件时，从 `DataPlaneConfigProvider` 拉取当前 Routes + Clusters，以 `CONFIG_REPORT` 事件返回。
3. **控制面新增实例配置查询 API**：`ControlPlane.Api` 添加 `GET /api/instances/{id}/config` 端点，通过 `EventSubscription` 通道向目标数据面发送查询事件，等待响应后返回结果。
4. **前端新增配置查看功能**：实例列表页每行增加"查看配置"按钮，调用上述 API，以 modal 展示当前配置的 Routes 和 Clusters 结构。
5. **纯 gRPC 通道实现，不增加数据面 HTTP 攻击面**。

## Capabilities

### New Capabilities
- `instance-config-query`: 通过 gRPC EventSubscription 双向通道查询数据面实例当前生效的 YARP 配置（Routes + Clusters），支持控制面 API 代理和前端展示。

### Modified Capabilities
- `in-memory-instance-store`: 实例管理能力扩展——除 CRUD 和心跳外，增加运行时配置查询能力。

## Impact

- **新增文件（2 个）**:
  - `src/MinGo.DataPlane/ConfigSync/ConfigQueryHandler.cs`（处理 CONFIG_QUERY 事件）
  - `src/MinGo.ControlPlane.Api/Services/InstanceConfigQueryService.cs`（控制面查询服务）
- **修改文件（4 个）**:
  - `src/MinGo.ControlPlane.Api/GrpcServices/Protos/dataplane.proto`（新增事件类型）
  - `src/MinGo.ControlPlane.Api/Controllers/InstancesController.cs`（新增 config 端点）
  - `src/MinGo.DataPlane/ConfigSync/DataPlaneConfigProvider.cs`（可能需暴露配置 DTO）
  - `frontend/min-go-console/src/pages/Instances.tsx`（增加配置查看 UI）
- **无需新增端口**：复用已有 gRPC EventSubscription 双向流
- **无需新增依赖**
