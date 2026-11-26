## Why

上一版「接入通道分组」把数据面分组建模为可选归属（未分配实例默认落在 `local` 全局通道），概念上仍保留了一个内置的「本地数据面」。本轮推翻该设计，明确对标 Portainer「环境（Environment）」语义：

- 接入通道只有两类资产：**数据面节点**（一组数据面实例的接入单元）与 **Cloudflare 隧道**（保持不变）。
- 系统启动时自动创建**默认节点**，存量全局路由挂到默认节点上，不再有隐形的 `local` 通道。
- **未绑定节点的实例不推送配置**（fail-closed）：数据面只有被显式挂到某个节点后才会收到规则；解绑时推送空快照清除旧规则。
- 路由发布时统一在节点与隧道之间选择；节点路由只下发给绑定该节点的实例。

## What Changes

- **数据面节点成为数据库资产**：`DataPlaneGroups`/`InstanceGroupAssignments` 两张表改名为 `DataPlaneNodes`/`InstanceNodeAssignments`（列 `GroupId` → `NodeId`），语义从「分组」改为「节点」。迁移内：插入默认节点（Id=`default`，名称「默认节点」，不可删除、可重命名）；存量路由 `ConnectionId` 改写——`local` → `node:default`、`group:<G>` → `node:<G>`；存量实例分配原样保留。
- **默认节点**：启动时 `EnsureDefaultNodeAsync` 幂等确保存在；删除接口对默认节点返回 400。存量 local 路由全部归属默认节点，全局广播语义由「默认节点路由」承接。
- **双向维护**：节点 CRUD（`GET/POST/PUT/DELETE /api/data-plane-nodes`，列表含实例数/在线数/路由数）；实例绑定端点 `PUT /api/instances/{id}/node`（null = 解绑）。接入通道页新增「数据面节点」区（与 Cloudflare 隧道区并列）；实例管理页行内节点下拉。两页同一 API、同一数据。
- **统一通道选择**：路由表单通道下拉 = 数据面节点 + Cloudflare 隧道；`local` 选项彻底移除（存量 local 路由已由迁移改写为默认节点）。
- **通道引用与校验**：`ConnectionId` ∈ `node:<NodeId>`（按 Id 引用，改名安全）/ 隧道 ID；保存路由校验通道存在（节点不存在或隧道不存在则 400）；删除被路由引用的节点返回 409 + 引用路由清单（fail-closed，不级联）。
- **隔离语义（本轮核心变化）**：`DataPlaneRuleCompiler.Compile(routes, targetNodeId)` 按节点过滤——隧道路由对所有**已绑定节点**的实例广播（隧道 ingress 回源需要数据面规则）；`node:<N>` 仅目标节点的实例收到；**未绑定实例（targetNodeId=null）收到空规则集**。广播逐连接按其节点组装，未绑定连接跳过；实例绑定变更 → 立即重推新快照，解绑 → 推送空快照清除旧规则。证书只发快照路由引用域名的证书（空快照 → 空证书）。
- **分配即时生效**：实例绑定变更 → 更新内存注册表 → 触发该实例快照重推。

## Capabilities

### New Capabilities

- `data-plane-nodes`: 数据面节点资产 CRUD（默认节点创建/保护）、实例绑定、接入通道页与实例页双向维护、路由通道存在性校验。

### Modified Capabilities

- `in-memory-instance-store`: 实例节点从分配表解析；`PUT /api/instances/{id}/node`；列表响应含节点；未绑定实例显示「未绑定」。
- `grpc-config-replication`: 快照按目标节点过滤路由与证书；未绑定实例不推送配置；广播从「推送所有数据面」改为「逐连接按其节点组装」；编译器契约从「按分组过滤」改写为「按节点过滤，未绑定为空」。

## Impact

- **MinGo.Core**：`DataPlaneGroupEntity`/`InstanceGroupAssignmentEntity` 改名 `DataPlaneNodeEntity`/`InstanceNodeAssignmentEntity`；`GatewayInstance`/`GatewayInstanceResponse` 的 `Group` 改 `Node`；`ConnectionIds` 移除 `local`/分组前缀，新增 `node:` 前缀与默认节点常量；`DataPlaneRuleCompiler` 契约改写（未绑定 → 空规则集）；`CertificateSnapshotPolicy` 不变（按快照路由域名过滤天然覆盖空快照）。
- **MinGo.Infrastructure**：`AppDbContext` 注册新表名；1 个 EF 迁移（表/列改名、默认节点种子、存量路由改写）；`IApiDbService` 节点 CRUD、分配读写、默认节点确保。
- **MinGo.Application**：`GatewayInstanceService` 节点解析与内存同步（`Node` 字段）。
- **MinGo.ControlPlane.Api**：`InstanceGroupsController` → `DataPlaneNodesController`（`/api/data-plane-nodes`）；`InstancesController` 绑定端点改 `/node`；路由保存通道校验覆盖 `node:*`；`ConfigReplicationService` 未绑定跳过/解绑空快照、按节点组装；`DataPlaneConnection.GroupId` → `NodeId`（未绑定为空串）；`CloudflareSyncService` 校验/同步/域名冲突排除 `node:*`；`TopologyService` 连接类型 `data-plane-node`、隧道路由 ingress 直连。
- **MinGo.DataPlane**：零改动。
- **frontend/min-go-console**：`types/index.ts`、`api/client.ts`、`Connections.tsx`（节点区、默认节点标识）、`Instances.tsx`（节点列/筛选/行内绑定）、`Routes.tsx`（节点+隧道下拉，无 local）。
- **兼容性**：1 个 EF 迁移（改名 + 种子 + 数据改写）；proto 零改动，老数据面不受影响；存量 `local` 与 `group:*` 路由经迁移落位默认节点/对应节点，行为等价（全局广播 → 默认节点广播）。
- **行为变化（有意为之）**：未绑定实例不再收到任何配置（旧行为：未分组实例收 local/隧道路由）；解绑实例收到空快照并停止转发。
- **非目标**：不做节点级访问控制；不做前缀自动绑定（实例量大时可再加回）；不做节点删除级联（拒绝 + 列出引用路由）；不改隧道 ingress 回源模型；不做同节点连接间的快照缓存优化。
