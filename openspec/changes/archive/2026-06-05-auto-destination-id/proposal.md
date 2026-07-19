## Why

当前创建/编辑集群时，用户需要手动为每个目标输入名称（ID）和地址。目标名称增加了认知负担和校验复杂度（不能重复、要有意义），但对反向代理路由没有实际价值——路由只关心目标地址。每次编辑时还需按名称匹配来保留健康状态，逻辑脆弱。

## What Changes

- **BREAKING**: `DestinationConfig.id` 改为由后端自动生成，格式为 `{clusterId}-{序列号}`，前端不再暴露 ID 输入框
- **BREAKING**: 前端目标编辑行只保留地址输入，去掉名称输入
- **BREAKING**: 目标不再通过 ID 匹配保留健康状态，地址改变视为新目标，健康状态重置
- 集群表单校验简化：去掉"目标 ID 不能重复"校验
- 集群卡片展示：地址作为主要标识，不再显示目标名称
- 不兼容现有数据，需清空重建

## Capabilities

### New Capabilities
- `auto-destination-id`: 后端自动生成目标 ID，格式 `{clusterId}-{序列号}`，序列号单调递增永不重复

### Modified Capabilities
- `destination-array-format`: destinations 仍然使用数组格式，但 id 由后端自动生成，提交时不再要求 id 字段
- `cluster-destination-validation`: 去掉"禁止重复目标 ID"校验规则；去掉"编辑时按索引保留健康状态"规则

## Impact

| 模块 | 影响 |
|---|---|
| `src/MinGo.Core/Entities/ProxyConfigEntities.cs` | ApiDestinationEntity 语义变化，id 不再由用户传入 |
| `src/MinGo.Core/Interfaces/IApiDbService.cs` | AddDestinationAsync 接口签名简化，去掉 destinationId 参数 |
| `src/MinGo.Application/Services/ApiManagementService.cs` | 适配新接口，创建目标时自动生成 ID |
| `src/MinGo.ReverseProxy/Controllers/ApiManagementController.cs` | 可能不需要改动（Service 层封装了逻辑） |
| `frontend/.../pages/Clusters.tsx` | 表单去掉 id 输入，只保留地址，简化校验 |
| `frontend/.../types/index.ts` | 类型不变，语义变化 |
| 现有数据库数据 | 不兼容，需清空重构 |
