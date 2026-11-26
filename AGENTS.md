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

## 接入通道分组（2026-11-26）

对标 Portainer 多环境管理，把「接入通道」做成可管理的持久资产。变更目录 `openspec/changes/2026-11-26-instance-access-groups/`，文档见 `docs/Server-Agent架构与部署.md` 第 7 节。

关键决策：
- **分组资产化**：`DataPlaneGroups` + `InstanceGroupAssignments` 落库（迁移 `20261126125556_AddDataPlaneGroups`）。路由按 `group:<GroupId>` 引用（按 Id 不按 name，重命名安全）。
- **双向维护**：分组 CRUD 在「接入通道」页；实例归属在「实例管理」页行内下拉 + `PUT /api/instances/{id}/group`（`null` 取消分组，未知组 400、未知实例 404）。分配是唯一写路径，PUT 即更新内存态 + 连接分组 + 该实例快照重推，心跳不做重解析。
- **隔离语义**：快照按连接分组组装（`DataPlaneConnection.GroupId`，未分配默认值 = `local`，即启动默认组）——隧道与存量 `local` 路由全数据面广播；`group:<G>` 只下发给该组；未分配实例只收 local/隧道路由，不收任何自定义分组路由。证书按快照路由引用的域名过滤（与 DP SNI 通配规则一致，私钥不跨组）；any-host 路由回退全量下发。
- **存量兼容**：新建路由不提供 `local`（编辑存量 local 显示「本地数据面（全部，存量）」）；proto 与数据面零改动，过滤全在 CP 侧。
- **fail-closed**：删除被路由引用的分组 → 409 + 引用路由清单；路由保存时 `group:*` 必须存在否则 400；`ValidateRouteAsync`/`SyncRouteChangeAsync`/`SyncRouteDeletedAsync`/拓扑均显式排除 `group:*`（不再误当隧道 ID）。

验证状态（2026-11-26 本机实测）：
- 全量单测 `dotnet test`：PASS，438/438（Core 177 + CP 115 + DP 146）。
- 前端 `pnpm build`：PASS。
- 浏览器实测：实例页组列/筛选/行内分配往返、通道页分组新建/重命名/删除（含 409 提示）、路由表单通道下拉（新建无 local、存量 local 编辑显示「存量」、分组路由编辑回填）。
- 未验证：双组数据面实测（8.3，需真实 DP 运行环境）。

已知坑（本机验证中发现并修复）：
- **SolidJS select 值竞态**：选项异步晚于 `value` 绑定时，浏览器会把 select 值重置为空且不再恢复（实例页行内分组、路由表单通道下拉都踩过）。修法：让 value 绑定响应式依赖选项信号（cell 内先读 `groups()`，或 `createMemo(() => { void props.connections; return connectionId(); })`）。
- **Promise.all 耦合**：接入通道页把分组加载与 Cloudflare 接口捆在一个 `Promise.all`，Cloudflare 未配置（503）时分组卡整体失效。已解耦为独立 try/catch。
- **group 通道误入隧道路径**：`ValidateRouteAsync` 原先只对 `local` 短路，分组路由在 Cloudflare 未配置时无法创建；域名冲突校验把跨组同域名误判为「多隧道冲突」。均已修复（分组与 local 同为数据面侧通道，同一域名可跨组并存）。
- 分组删除 409 的 message 经前端 `client.ts` 提取（`payload.message`），列表接口 `/api/instance-groups` 返回 `instanceCount`/`onlineCount`/`routeCount`。

