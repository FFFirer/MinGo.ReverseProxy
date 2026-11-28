## 1. 数据模型与迁移

- [x] 1.1 `DataPlaneGroupEntity`/`InstanceGroupAssignmentEntity` 改名 `DataPlaneNodeEntity`/`InstanceNodeAssignmentEntity`（列 `GroupId`→`NodeId`），新增 1 个 EF 迁移：表/列/索引改名、插入默认节点（Id=`default`）、存量路由改写（`local`→`node:default`、`group:X`→`node:X`）
- [x] 1.2 `IApiDbService` 分组 CRUD 改名节点 CRUD；新增 `EnsureDefaultNodeAsync`（幂等）与「按节点查引用路由」；删除默认节点返回明确拒绝

## 2. 通道值域与编译器

- [x] 2.1 `ConnectionIds` 重写：移除 `local`/`GroupPrefix`/`IsGroup`/`GetGroupId`/`ForGroup`，新增 `NodePrefix = "node:"`、`DefaultNode = "default"`、`IsNode`、`GetNodeId`、`ForNode`；核对 `ApiManagementController.PublishAsync`、`CloudflareSyncService`、`RouteUniquenessPolicy`、`TopologyService` 等全部调用点
- [x] 2.2 `DataPlaneRuleCompiler.Compile(routes, targetNodeId)` 契约改写：`targetNodeId == null`（未绑定）→ 空规则集；否则隧道路由全保留 + `node:<N>` 仅目标节点保留
- [x] 2.3 单测：隧道路由对已绑定实例全保留、节点路由按目标节点过滤、未绑定实例空规则集、去重契约不受影响

## 3. 快照按连接组装与广播

- [x] 3.1 `DataPlaneConnection.GroupId` → `NodeId`（未绑定为空串），注册时从实例解析回填
- [x] 3.2 注册/订阅：未绑定节点 → 不发送初始化快照；绑定 → 按节点组装发送
- [x] 3.3 广播：跳过未绑定连接，按 `NodeId` 分组组装快照
- [x] 3.4 解绑：推送空快照（无路由、无证书）清除旧规则；绑定：立即重推新快照
- [x] 3.5 证书过滤沿用按快照路由域名筛选（空快照 → 空证书）

## 4. 节点解析与即时生效

- [x] 4.1 节点解析放在调用方而非单例：`ConfigReplicationService`（scope factory）与 `InstancesController.RegisterInstance`（scoped）在注册时按分配表解析 `NodeId`/`NodeName`，经 `GatewayInstanceService.UpdateInstanceNodeAsync` 写入内存态；`GatewayInstance` 与 `GatewayInstanceResponse` 加 `NodeId`/`NodeName`（绑定只经 PUT 变更，心跳无需重复解析）
- [x] 4.2 绑定变更（`PUT /api/instances/{id}/node`）→ 更新内存实例 + 连接节点 + 触发该实例快照重推（解绑推空快照）

## 5. API

- [x] 5.1 `DataPlaneNodesController`：`GET /api/data-plane-nodes`（含各节点实例数/在线数）、`POST`、`PUT /{id}`（重命名）、`DELETE /{id}`（默认节点 400；被路由引用时 409 + 引用路由清单）
- [x] 5.2 `PUT /api/instances/{id}/node`：`{nodeId}` 或 `null`（解绑），未知节点 400、未知实例 404
- [x] 5.3 路由保存校验：`ConnectionId` 为 `node:*` 时节点必须存在，否则 400

## 6. 前端

- [x] 6.1 `types/index.ts` 与 `api/client.ts`：节点类型、`getDataPlaneNodes`、`createDataPlaneNode`、`renameDataPlaneNode`、`deleteDataPlaneNode`、`setInstanceNode`
- [x] 6.2 `Instances.tsx`：「接入节点」列（未绑定显示「未绑定」）、按节点筛选、行内节点下拉（含「未绑定」与「新建节点…」入口）
- [x] 6.3 `Connections.tsx`：新增「数据面节点」区（列表/添加/重命名/删除/成员数），默认节点标「默认」且禁删，与 Cloudflare 隧道区并列；删除被引用节点时展示引用路由提示（后端 409 消息）
- [x] 6.4 `Routes.tsx`：通道下拉 = 节点（含默认节点）+ 隧道，彻底移除 `local`；通道列渲染 `node:*` 为节点名

## 7. 规范与文档

- [x] 7.1 delta specs：`specs/instance-groups/` → `specs/data-plane-nodes/`，三个 spec 文件按新语义改写（未绑定不推送、默认节点、节点+隧道两通道）
- [x] 7.2 `docs/Server-Agent架构与部署.md` 第 7 节改写为「数据面节点」：节点资产化、默认节点、双向维护、未绑定不推送/解绑空快照、证书随节点过滤

## 8. 验证

- [x] 8.1 `dotnet test` 全绿
- [x] 8.2 `pnpm build` 通过；浏览器验证：实例页节点列/筛选/行内绑定、通道页节点维护（默认节点禁删、409 提示）、路由表单节点+隧道下拉、未绑定实例不推送配置
- [ ] 8.3 双节点数据面实测：各自只收到本节点路由与证书，`GET /api/instances/{id}/config` 确认过滤生效（需真实 DP 运行环境）
