# AGENTS.md — 项目上下文

## Server/Agent 关系整理（2026-11-25）

对标 Portainer Server ↔ Agent 关系，把 CP/DP 定位与加入流程补齐。权威文档：`docs/Server-Agent架构与部署.md`（旧 `docs/技术设计方案.md` 已归档至 `docs/archived/`）。

关键决策：
- **数据面 = Agent**：只有 DP 主动拨 CP gRPC（ConfigReplication/Heartbeat/Event 三条双向流），Agent 侧零入站端口，适配远端主机。
- **稳定身份**：`Agent:Id` 配置 > `/app/data/agent-id` 状态文件（挂卷）> 随机值。此前每次重启换身份，实例列表积累孤儿、统计断裂。
- **加入凭据（edge key 类比）**：CP `DataPlane:SharedSecrets`（列表）+ DP `ControlPlane:SharedSecret`，`x-dp-secret` metadata 校验。未配置=直通（单机行为不变），配置即 fail-closed。5001 对外暴露必须启用——配置快照携带证书私钥。
- **实例注册表不落库**：内存化是有意决策（spec `in-memory-instance-store`），实例是运行时状态不是配置资产。
- **命令通道**：心跳响应流下发 `reload-config`/`reload-certs`/`shutdown`；`shutdown` 走 `StopApplication()`，docker 重启策略下等效重启。此前 proto 定义了命令但 CP 从不发送（死契约）。
- **Agent 独立部署**：`docker-compose.agent.yml` + 文档一行 `docker run`，对标 Portainer "Add environment"。

验证状态（2026-11-25 本机实测）：
- 后端 `dotnet build`：PASS，0 错误。
- 全量单测 `dotnet test`：PASS，426/426（Core 167 + CP 113 + DP 146；含新增认证拦截器/身份解析 13 例）。
- 前端 `pnpm build`：PASS（vite 7.3.5）。
- 浏览器验证：PASS——实例页显示自描述元数据（版本 1.0.0 而非占位符）、重载命令端到端（DP 日志收到 CmdReloadConfig）。关停命令未做浏览器级验证：自动化安全分类器拦截了触发数据面关停的动作，但其命令送达链路与已验路径同构，差异仅在 `StopApplication()` 一行。

本地验证命令：
```bash
dotnet build MinGo.ReverseProxy.slnx
dotnet test MinGo.ReverseProxy.slnx
cd frontend/min-go-console && pnpm build
```

升级迁移坑：
- 老数据面连新控制面：订阅自描述字段留空，CP 回退占位名，不影响协议兼容。
- 控制面启用 `SharedSecrets` 后，存量数据面必须同步配 `ControlPlane__SharedSecret`，否则全部拒连（fail-closed，设计如此）。
- 数据面新增 `./data:/app/data` 挂载（agent-id 持久化），升级 compose 时注意不要覆盖已有 `mingocp.db` 目录结构——agent-id 与 db 文件并存无冲突。

## 接入通道：数据面节点 + Cloudflare 隧道（2026-11-26，重设计后）

对标 Portainer 多环境管理，接入通道 = 数据面节点（落库资产）+ Cloudflare 隧道（不落库，经 CF API 管理）。本设计**取代**同日先发布的「接入通道分组」设计（v1.17.0）：分组表就地改名成节点表，`local`/`group:X` 路由由迁移改写。变更目录 `openspec/changes/2026-11-26-instance-access-groups/`，文档见 `docs/Server-Agent架构与部署.md` 第 7 节。

关键决策：
- **节点资产化**：`DataPlaneNodes` + `InstanceNodeAssignments` 落库（迁移 `20261126161553_DataPlaneNodes`：表/列改名 + 插入默认节点 + 路由改写 `local`→`node:default`、`group:X`→`node:X`）。启动时 `EnsureDefaultNodeAsync` 幂等兜底。默认节点 Id=`default` 不可删除（400）、可重命名。
- **双向维护**：节点 CRUD 在「接入通道」页（`/api/data-plane-nodes`）；实例绑定在「实例管理」页行内下拉 + `PUT /api/instances/{id}/node`（`null` 解绑，未知节点 400、未知实例 404）。绑定是唯一写路径，PUT 即更新内存态 + 连接节点 + 该实例快照重推，心跳不做重解析。
- **未绑定不推送（fail-closed）**：`DataPlaneConnection.NodeId` 未绑定为空串——注册时不发初始化快照、广播时跳过、解绑推空快照（无路由无证书）清残留。`DataPlaneRuleCompiler.Compile(routes, null)` 返回空规则集；证书经 `SelectForDelivery` 空 routeHosts 同样全过滤（`All` 对空集合为真）。
- **隔离语义**：隧道路由对所有已绑定节点的实例广播（隧道 ingress 回源数据面，规则必须装在数据面）；`node:<N>` 只下发给该节点。同一域名可跨节点并存（节点同为数据面侧通道）；`ValidateRouteAsync`/`SyncRouteChangeAsync`/`SyncRouteDeletedAsync`/拓扑均按 `!IsNode` 判隧道。拓扑不再有 `conn:local` 实体节点——ingress 边从通道直达路由，隧道中转在「转发路径」文案里说明。
- **路由表单**：通道下拉 = 节点（含默认节点）+ 隧道，彻底移除 `local`；`RouteConfig.ConnectionId` 默认 `node:default`；保存 `node:*` 必须节点存在否则 400（`ValidateNodeChannelAsync`）。

验证状态（2026-11-27 本机实测）：
- 全量单测 `dotnet test`：PASS，439/439（Core 179 + CP 114 + DP 146）。
- 前端 `pnpm build`：PASS。
- 浏览器实测（PASS，Production 环境 + 真实开发库 gateway.db）：
  - 实例页：节点列（未绑定/默认节点）、按节点筛选（全部/未绑定/默认节点）、行内绑定往返（下拉选默认节点 → API 回读 `nodeId=default`/`nodeName=默认节点`）。
  - 通道页：默认节点「默认」badge + 删除按钮禁用；创建/重命名节点往返；删除被引用节点 → 409，`{"message":"节点仍被 1 条路由引用，请先调整这些路由的接入通道","routes":["edge-test"]}`。
  - 路由表单：通道下拉 = 数据面节点（含默认节点）+ 隧道提示，无 `local`/`group`；存量路由（迁移改写 `local`→`node:default`）回填正确；`node:*` 不存在的节点保存 → 400。
  - 未绑定不推送：CP 日志「Data plane 4ce732c9 is not bound to any node, no config pushed」；绑定后 DP 收到快照（FullSync）；解绑后实例 `nodeId` 为空（空快照推送由单测覆盖）。
  - 顺带发现并修复一个**既有** toast 不显示 bug（详见已知坑）。
- 未验证：双节点数据面实测（需真实 DP 运行环境）。

已知坑（沿用并修复）：
- **SolidJS 组件函数体读信号不响应**：`createComponent` 编译为 `untrack(() => Comp(props))`，函数体内 `const items = toasts(); if (items.length === 0) return null;` 的提前返回永远不更新——所有 toast 从不显示（与节点重设计无关的既有 bug）。修法：把信号读取移入返回 JSX（`<Show when={toasts().length > 0}><For each={toasts()}>…`），或 `createMemo`。浏览器实测成功/错误 toast 均正常渲染。
- **SolidJS select 值竞态**：选项异步晚于 `value` 绑定时，浏览器会把 select 值重置为空且不再恢复（实例页行内节点、路由表单通道下拉都踩过）。修法：让 value 绑定响应式依赖选项信号（cell 内先读 `nodes()`，或 `createMemo(() => { void props.connections; return connectionId(); })`）。
- **Promise.all 耦合**：接入通道页把节点加载与 Cloudflare 接口捆在一个 `Promise.all`，Cloudflare 未配置（503）时节点卡整体失效。已解耦为独立 try/catch。
- **节点通道误入隧道路径**：`ValidateRouteAsync` 原先只对 `local` 短路，节点路由在 Cloudflare 未配置时无法创建；域名冲突校验把跨节点同域名误判为「多隧道冲突」。均已修复。
- 节点删除 409 的 message 经前端 `client.ts` 提取（`payload.message`），列表接口 `/api/data-plane-nodes` 返回 `instanceCount`/`onlineCount`/`routeCount`。

