## Why

当前 API 的 destinations 使用 `Dictionary<string, DestinationConfig>` 格式，导致前端表单需要在数组和字典之间反复转换，代码复杂且容易出错。改为数组格式后前后端数据结构对齐，减少转换逻辑。

## What Changes

- `ClusterConfig.Destinations` 从 `Dictionary<string, DestinationConfig>` 改为 `List<DestinationConfig>`
- `DestinationConfig` 新增 `Id` 字段
- API JSON 格式从 `{"key": {"address":"...","healthy":true}}` 改为 `[{"id":"key","address":"...","healthy":true}]`
- 前端类型定义同步修改，去掉数组↔字典转换逻辑
- **BREAKING**: API 格式变更，前后端需同步发布

## Capabilities

### New Capabilities

- `destination-array-format`: 集群目标使用数组格式的定义和交互

### Modified Capabilities

无

## Impact

- `MinGo.Shared/Models/GatewayConfig.cs` — 模型定义
- `MinGo.Core/Models/GatewayConfig.cs` — 模型定义
- `MinGo.Infrastructure/Data/ApiDbService.cs` — 映射和 CRUD
- `frontend/.../types/index.ts` — 前端类型
- `frontend/.../pages/Clusters.tsx` — 表单和展示逻辑
