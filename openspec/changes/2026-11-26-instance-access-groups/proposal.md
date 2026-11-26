## Why

多地区部署（模式 B：Server 中心 + 多 Agent 远端）下，实例页平铺无组织结构，且「接入通道」目前只有硬编码的 `local`（全部本地数据面）与 Cloudflare 隧道两类，本地通道无法按地区划分，分组停留在静态配置映射（无法在界面维护、无法作为持久资产被路由引用）。本次优化：**接入通道成为可管理资产**——支持添加 Cloudflare 隧道或数据面分组；分组可在接入通道页与实例管理页双向维护；路由发布时统一选择接入通道。分组即本地通道，数据面只安装本组通道的规则（真流量隔离），证书私钥随组过滤。

## What Changes

- **数据面分组成为数据库资产**：新增 `DataPlaneGroups` 表（Id/Name/CreatedAt/UpdatedAt）与 `InstanceGroupAssignments` 表（InstanceId → GroupId）；**取代原 `DataPlane:InstanceGroups` 静态前缀映射**（该设计从未实施，直接删除，对标 Portainer Endpoint Groups 形状）。
- **双向维护**：分组 CRUD API（`GET/POST/PUT/DELETE /api/instance-groups`，列表返回各组实例数/在线数）；实例分配端点 `PUT /api/instances/{id}/group`（null = 取消分组）。接入通道页新增「数据面分组」区（添加/重命名/删除/成员数），与既有 Cloudflare 隧道区并列；实例管理页行内分组下拉 + 新建分组入口。两页同一 API、同一数据。
- **统一通道选择**：路由表单通道下拉 = 数据面分组 + Cloudflare 隧道；新建路由不再提供 `local`；存量 local 路由保持全局，编辑时显示「本地（全部，存量）」。
- **通道引用与校验**：`ConnectionId` ∈ `local`（存量）/ `group:<GroupId>`（按 Id 引用，改名安全）/ 隧道 ID；保存路由校验通道存在（分组不存在或隧道不存在则 400）；删除分组时若被路由引用则拒绝并列出引用路由（fail-closed，不做级联）。
- **隔离语义（沿用上轮）**：`DataPlaneRuleCompiler.Compile(routes, targetGroup)` 按组过滤——隧道与 local 路由全保留，`group:<G>` 仅目标组保留，未分组实例不装分组路由；锁定契约与 `Compile_KeepsTunnelBoundRoutes` 随本次设计改写（隧道全保留语义不变）；广播逐连接按组组装；证书只发快照路由引用域名的证书。
- **分配即时生效**：实例分组变更 → 更新内存注册表 → 触发该实例快照重推。

## Capabilities

### New Capabilities

- `instance-groups`: 数据面分组资产 CRUD、实例分配、接入通道页与实例页双向维护、路由通道存在性校验。

### Modified Capabilities

- `in-memory-instance-store`: 实例组从分配表解析（替代配置映射）；`PUT /api/instances/{id}/group`；列表响应含组；前端组列/筛选与行内分配。
- `grpc-config-replication`: 快照按目标组过滤路由与证书；广播从「推送所有数据面」改为「逐连接按其组组装」；编译器契约从「不读接入通道」改写为「按通道过滤」。

## Impact

- **MinGo.Core**：新增 `DataPlaneGroupEntity`、`InstanceGroupAssignmentEntity`；`GatewayInstance`/`GatewayInstanceResponse` 加 `Group`（Id + Name）；`ConnectionIds` 扩展；`DataPlaneRuleCompiler` 签名与过滤；`CertificateSnapshotPolicy` 增加按路由引用域名过滤。
- **MinGo.Infrastructure**：`AppDbContext` 注册新表 + 1 个 EF 迁移；`IApiDbService` 分组 CRUD、分配读写、按 InstanceId 查组。
- **MinGo.Application**：`GatewayInstanceService` 注入 `IApiDbService`，注册/心跳解析组；组变更触发配置重推。
- **MinGo.ControlPlane.Api**：`InstanceGroupsController`（CRUD）；`InstancesController` 分配端点；路由保存通道校验；`ApiManagementController.PublishAsync` 本地类通道判断覆盖 `group:*`；`ConfigReplicationService` 合并组装、按组编译、逐连接广播；`DataPlaneConnection` 加 `Group`。
- **MinGo.DataPlane**：零改动。
- **frontend/min-go-console**：`types/index.ts`、`api/client.ts`、`Instances.tsx`（组列/筛选/行内分配/新建分组）、`Connections.tsx`（分组通道区）、`Routes.tsx`（统一通道下拉）。
- **兼容性**：1 个 EF 迁移（两张新表）；proto 零改动，老数据面不受影响；存量 local 与隧道路由行为不变；未分组实例不装分组路由（fail-closed）。
- **非目标**：不做组级访问控制；不做前缀自动入组（实例量大时可再加回）；不做分组删除级联（拒绝 + 列出引用路由）；不改隧道 ingress 回源模型；不做同组连接间的快照缓存优化。
