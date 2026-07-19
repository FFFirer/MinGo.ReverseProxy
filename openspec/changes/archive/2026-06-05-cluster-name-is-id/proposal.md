## Why

当前集群同时存在 `Id`（后端自动生成的 GUID）和 `Name`（用户输入的名称）两个字段。GUID 对外不可读，路由引用、目标 ID 都基于 GUID，导致调试和日志排查时需要来回映射。与 destination auto-ID 一样，这是"多余标识符"问题——集群名称天然是唯一标识，应该直接用作 ID。

## What Changes

- **BREAKING**: `ApiClusterEntity.Id` 改为用户输入的集群名称，不再自动生成 GUID
- **BREAKING**: 移除 `ApiClusterEntity.Name` 属性，名称即 ID
- **BREAKING**: 集群名称不能修改（选项 A），改名需删除重建
- `CreateClusterAsync` 不再调用 `Guid.NewGuid()`，直接用用户传入的 `Id`
- 路由 `ClusterId` 的值从 GUID 变为人类可读的集群名称
- 目的地自动 ID 从 `{GUID}-1` 变为 `{名称}-1`，更具可读性
- 前端集群表单：输入的名称直接作为 ID，不再有隐藏的 GUID 字段
- 不兼容现有数据，需清空重建

## Capabilities

### New Capabilities
- `cluster-name-is-id`: 集群名称即集群 ID，移除 Name 字段，Id 由用户指定而非后端生成

### Modified Capabilities
- `auto-destination-id`: 目标 ID 格式从 `{clusterGuid}-{seq}` 变为 `{clusterName}-{seq}`（间接受益）
- `cluster-form-focus`: 集群表单不再有"集群名称"和"集群 ID"的区分，只有一个输入框
- `seed-data`: 样本数据的集群 ID 使用可读名称

## Impact

| 模块 | 影响 |
|---|---|
| `src/MinGo.Core/Entities/ProxyConfigEntities.cs` | ApiClusterEntity 移除 Name，Id 语义变化 |
| `src/MinGo.Core/Models/GatewayConfig.cs` | ClusterConfig 移除 Name 属性 |
| `src/MinGo.Infrastructure/Data/ApiDbService.cs` | CreateClusterAsync 不再生成 GUID，直接用 Id |
| `src/MinGo.Infrastructure/Data/AppDbContext.cs` | 可能需调整主键配置 |
| `frontend/.../pages/Clusters.tsx` | 表单去掉 Name 输入，只保留一个名称/ID 输入 |
| `frontend/.../pages/Routes.tsx` | 下拉框 `{c.name || c.id}` → `{c.id}` |
| `frontend/.../types/index.ts` | ClusterConfig 移除 name 字段 |
| 现有数据库数据 | 不兼容，需清空重构 |
