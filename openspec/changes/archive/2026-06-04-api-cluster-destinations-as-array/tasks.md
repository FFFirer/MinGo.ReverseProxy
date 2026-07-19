## 1. 后端模型定义

- [x] 1.1 修改 `MinGo.Shared/Models/GatewayConfig.cs` — `ClusterConfig.Destinations` 改为 `List<DestinationConfig>`，`DestinationConfig` 增加 `Id`
- [x] 1.2 修改 `MinGo.Core/Models/GatewayConfig.cs` — 同上

## 2. 后端映射和 CRUD

- [x] 2.1 修改 `ApiDbService.cs` 映射方法支持数组格式
- [x] 2.2 修改 `ApiDbService.cs` CRUD 和示例数据使用数组

## 3. 前端类型和表单

- [x] 3.1 修改 `types/index.ts` — `ClusterConfig.destinations` 改为数组，`DestinationConfig` 增加 `id`
- [x] 3.2 修改 `Clusters.tsx` — 表单和卡片展示直接使用数组

## 4. 验证

- [x] 4.1 验证后端 build 通过
- [x] 4.2 验证前端 build 通过
