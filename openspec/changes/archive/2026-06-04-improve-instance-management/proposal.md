## Why

当前 `GatewayInstanceService` 所有方法均为空桩（stub），数据面实例无法通过管理控制台查看状态和指标。HeartbeatCollectService 收到心跳后仅更新内存连接管理器，不更新实例服务，导致 REST API 和前端一直返回空数据。需要补全这个断层，使实例管理功能可用。

## What Changes

- **GatewayInstanceService 改为 Singleton + 内存实现**：用 `ConcurrentDictionary` 替代空桩，所有实例数据和状态仅存内存，不入数据库
- **HeartbeatCollectService 联动 IGatewayInstanceService**：gRPC 心跳到达时同步更新实例的 CPU/内存/请求数/心跳时间
- **ConfigReplicationService 联动实例注册**：数据面 gRPC 连接时自动注册实例，流断开时标记 Offline
- **前端 Instances 页面增强**：展示 CPU/内存/请求数/Uptime/错误率等指标，添加删除操作
- **超时检测**：获取列表时自动检测 30 秒无心跳的实例标记为 HeartbeatTimeout

## Capabilities

### New Capabilities
- `in-memory-instance-store`: 基于内存的 GatewayInstance 存储，支持注册/心跳更新/超时检测/CRUD

### Modified Capabilities
- （无修改现有 spec）

## Impact

**修改文件**：
- `src/MinGo.Application/Services/GatewayInstanceService.cs` — 完全重写
- `src/MinGo.ControlPlane.Api/Program.cs` — 改为 Singleton 注册
- `src/MinGo.ControlPlane.Api/GrpcServices/HeartbeatCollectService.cs` — 注入 IGatewayInstanceService
- `src/MinGo.ControlPlane.Api/GrpcServices/ConfigReplicationService.cs` — 注入 IGatewayInstanceService，连接/断开时注册/注销
- `frontend/min-go-console/src/pages/Instances.tsx` — 展示增强
- `frontend/min-go-console/src/types/index.ts` — 可能补充字段

**不修改**：
- 数据库：无 Migration、无新 Entity
- REST API 端点：`InstancesController` 保持不变
- gRPC proto：不修改协议
