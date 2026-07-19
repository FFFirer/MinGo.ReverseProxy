## Why

当前集群表单的 destinations 虽然在信号层使用数组存储、提交时转字典，但存在两个问题：1) 重复 ID 会被静默覆盖；2) 编辑时修改目标名称会导致健康状态丢失。需要增加校验并优化转换逻辑。

## What Changes

- 在 `ClusterFormModal` 中增加目标 ID 重复校验，提交时拦截重复 ID
- 提交时数组转字典逻辑增加安全兜底：当 ID 为空时使用索引作为 key
- 编辑场景下，健康状态匹配改为按数组索引匹配而非按 ID 匹配，确保修改 ID 不丢失健康状态
- 仅在 `Clusters.tsx` 中修改，不改动 API 或类型定义

## Capabilities

### New Capabilities
- `cluster-destination-validation`: 集群目标表单的 ID 重复校验和数据转换

### Modified Capabilities

无

## Impact

- 仅修改 `frontend/min-go-console/src/pages/Clusters.tsx` 一个文件
- 无 API/后端变更
- 无新增依赖
