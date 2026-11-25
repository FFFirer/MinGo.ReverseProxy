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
