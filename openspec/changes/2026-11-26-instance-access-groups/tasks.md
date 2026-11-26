## 1. 数据模型与迁移

- [x] 1.1 新增 `DataPlaneGroupEntity`（Id/Name/CreatedAt/UpdatedAt）与 `InstanceGroupAssignmentEntity`（InstanceId→GroupId），注册进 `AppDbContext` 并生成 1 个 EF 迁移
- [x] 1.2 `IApiDbService` 增加分组 CRUD、分配读写（按 InstanceId 查/设/删组、按 GroupId 数实例）与「按组查引用路由」

## 2. 通道值域与编译器

- [x] 2.1 `ConnectionIds` 扩展：`GroupPrefix = "group:"`、`IsGroup`、`GetGroupId`、`ForGroup`；核对 `ApiManagementController.PublishAsync` 等全部调用点，`group:*` 不触发隧道发布
- [x] 2.2 `DataPlaneRuleCompiler.Compile(routes, targetGroup)` 按组过滤：隧道与 `local` 全保留，`group:<G>` 仅目标组保留；remarks 契约改写
- [x] 2.3 单测：隧道路由全保留（`Compile_KeepsTunnelBoundRoutes` 语义不变）、`local` 全保留、分组路由按目标组过滤、未分组实例不装分组路由、去重契约不受影响

## 3. 快照按连接组装与广播

- [x] 3.1 `DataPlaneConnection` 增加 `GroupId` 字段，注册时从实例解析回填
- [x] 3.2 合并 `BuildConfigSnapshotAsync` 与 `BroadcastConfigUpdateAsync` 为按目标组组装的单一口径
- [x] 3.3 广播改为逐连接按其组组装快照发送（`DataPlaneConnectionManager.SendConfigAsync` 逐连接发送）
- [x] 3.4 证书按组过滤：`CertificateSnapshotPolicy` 扩展「按快照路由引用的域名筛选」（与数据面 SNI 通配匹配同一规则）

## 4. 组解析与即时生效

- [x] 4.1 组解析放在调用方而非单例：`ConfigReplicationService`（scope factory）与 `InstancesController.RegisterInstance`（scoped）在注册时按分配表解析 `GroupId`/`GroupName`，经 `GatewayInstanceService.UpdateInstanceGroupAsync` 写入内存态；`GatewayInstance` 与 `GatewayInstanceResponse` 加 `GroupId`/`GroupName`（分配只经 PUT 变更，心跳无需重复解析）
- [x] 4.2 分配变更（`PUT /api/instances/{id}/group`）→ 更新内存实例 + 连接分组 + 触发该实例快照重推

## 5. API

- [x] 5.1 `InstanceGroupsController`：`GET /api/instance-groups`（含各组实例数/在线数）、`POST`、`PUT /{id}`（重命名）、`DELETE /{id}`（被路由引用时 409 + 引用路由清单）
- [x] 5.2 `PUT /api/instances/{id}/group`：`{groupId}` 或 `null`（取消分组），未知组 400、未知实例 404
- [x] 5.3 路由保存校验：`ConnectionId` 为 `group:*` 时组必须存在，否则 400

## 6. 前端

- [x] 6.1 `types/index.ts` 与 `api/client.ts`：分组类型、`getInstanceGroups`、`createInstanceGroup`、`renameInstanceGroup`、`deleteInstanceGroup`、`setInstanceGroup`
- [x] 6.2 `Instances.tsx`：「接入分组」列（未分配显示「未分组」）、按组筛选、行内分组下拉（含「未分组」与「新建分组…」入口）
- [x] 6.3 `Connections.tsx`：新增「数据面分组」区（列表/添加/重命名/删除/成员数），与 Cloudflare 隧道区并列；删除被引用分组时展示引用路由提示（后端 409 消息）
- [x] 6.4 `Routes.tsx`：通道下拉 = 分组 + 隧道，新建不提供 `local`；存量 local 路由编辑时显示「本地（全部，存量）」；通道列渲染 `group:*` 为组名

## 7. 规范与文档

- [x] 7.1 delta specs：新增 `specs/instance-groups/spec.md`；修改 `specs/in-memory-instance-store/spec.md`、`specs/grpc-config-replication/spec.md`（本 change 目录内）
- [x] 7.2 `docs/Server-Agent架构与部署.md` 补「接入通道分组」小节：分组资产化、双向维护、隔离语义、存量 local 兼容与证书随组过滤

## 8. 验证

- [x] 8.1 `dotnet test` 全绿
- [x] 8.2 `pnpm build` 通过；浏览器验证：实例页组列/筛选/行内分配、通道页分组维护（新建/重命名/删除/409 提示）、路由表单统一通道选择（新建无 local、存量 local 显示「存量」、分组路由回填）
- [ ] 8.3 双组数据面实测：各自只收到本组路由与证书，`GET /api/instances/{id}/config` 确认过滤生效
