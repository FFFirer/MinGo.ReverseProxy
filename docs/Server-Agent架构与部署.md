# Server / Agent 架构与部署

MinGo.ReverseProxy 的组件关系对标 **Portainer Server ↔ Portainer Agent**：控制面（Control Plane）是管理中枢，数据面（Data Plane）是部署在流量入口侧的轻量代理（Agent）。本文是 CP/DP 关系模型的权威文档；旧的设计草案已归档到 `docs/archived/技术设计方案.md`。

## 1. 角色定位对照

| Portainer | MinGo | 职责 |
| --- | --- | --- |
| Server（UI + API + DB） | `src/MinGo.ControlPlane.Api` | REST API（5000）+ 管理控制台 + gRPC 接入（5001）+ SQLite 配置存储 |
| Agent（被管主机上的轻量代理） | `src/MinGo.DataPlane` | YARP 流量代理（8080/8443），无本地配置存储，全部配置来自控制面 |
| Edge Agent 主动外连 Server | 数据面主动拨控制面 gRPC | Agent 侧零入站端口，适配 NAT/防火墙后的远端主机 |
| edge key 加入校验 | `DataPlane:SharedSecrets` + `ControlPlane:SharedSecret` | 见 §4 加入流程 |
| Server 环境注册表 | `GatewayInstanceService`（内存） | 有意不落库：运行时状态，随控制面重启重建（`in-memory-instance-store` spec） |

## 2. 连接模型

数据面与控制面之间是 **Agent 主动外连** 的三条 gRPC 双向流（`dataplane.proto`），全部从数据面发起：

```
┌──────────────────┐                  ┌──────────────────────────┐
│  Control Plane   │  ←──gRPC:5001──  │  Data Plane (Agent)      │
│  REST 5000       │                  │  代理 8080 / 8443        │
│  前端控制台       │                  │  ConfigSyncService       │
│  SQLite          │                  │  HeartbeatReporter       │
└──────────────────┘                  │  ConfigQueryHandler      │
        │                             └──────────────────────────┘
        │  1. ConfigReplication：DP 订阅 → CP 推送配置快照（路由/集群/证书）
        │  2. HeartbeatCollect：DP 每 10s 上报指标/访问日志 → CP 回执中可携带命令
        │  3. EventSubscription：双向事件（CONFIG_QUERY/CONFIG_REPORT 等）
        ▼
   断线重连：DP 按 1s→2s→5s→10s→30s 退避重试；重连后按版本号重新全量同步
```

- 方向约定：**只有数据面拨号控制面**，控制面从不主动连数据面。配置下发、命令下发都复用既有连接（推送式）。
- 数据面重启身份不变（§3），控制面重启后注册表重建、数据面自动重连补注册。

## 3. 身份与自描述

- **稳定身份**：`Agent:Id` 配置 > `/app/data/agent-id` 状态文件（挂卷持久化）> 临时随机值。重启不变，保证实例列表与遥测统计连续。
- **自描述上报**：订阅时携带 `name` / `version` / `listener_addresses`（proto 追加字段，老数据面留空即未传输）。控制面用这些值注册实例，IP 取 gRPC 连接对端，不再伪造 `DataPlane-xxx` / `unknown`。

## 4. 加入流程（对标 edge key）

1. 控制面配置 `DataPlane__SharedSecrets__0=<secret>`（列表，支持多密钥轮换）。
2. 数据面配置 `ControlPlane__SharedSecret=<secret>`，三条 gRPC 流全部附加 `x-dp-secret` metadata。
3. 控制面 `DataPlaneAuthInterceptor` 校验：**未配置密钥 = 直通**（本地/单机默认，行为不变）；**配置即 fail-closed**——缺失或不匹配一律 `PermissionDenied` 拒连。
4. 密钥匹配后按正常流程订阅 → 下发全量快照 → 实例出现在控制台实例页。

安全边界：配置快照携带证书私钥，`5001` 一旦对外暴露**必须**配置密钥；如需传输层加密，`ControlPlane:GrpcUrl` 支持 `https://`（前置 TLS 终结或 Kestrel 配证书），应用层校验与传输层加密互不替代。

## 5. 命令通道（Server → Agent 操作）

`POST /api/instances/{id}/commands`，body `{ "type": "reload-config" | "reload-certs" | "shutdown" }`，前端实例页有对应按钮（离线禁用）：

| 命令 | 数据面行为 |
| --- | --- |
| `reload-config` | 确认性命令；配置本就随推送实时生效，流断开重连时按版本号全量重同步 |
| `reload-certs` | 确认性命令；证书随配置快照推送 |
| `shutdown` | `IHostApplicationLifetime.StopApplication()` 优雅停进程；docker 重启策略下等效重启 |

送达通道是心跳双向流的响应侧；实例无心跳通道时接口返回 503。

## 6. 部署模式

### 模式 A：单机（默认）

`docker-compose.yml`：migrate → control-plane → data-plane 同机编排，密钥默认不启用。

### 模式 B：Server 中心 + 多 Agent 远端（对标 Portainer 分布式）

1. 中心部署控制面，`5000/5001` 对 Agent 网络可达，配置 `DataPlane__SharedSecrets__0`。
2. 每台远端主机按 `docker-compose.agent.yml` 部署数据面（或等价的一行命令）：

```bash
docker run -d --name min-go-agent \
  -p 8080:8080 -p 8443:8443 \
  -e ControlPlane__GrpcUrl=http://<CP_HOST>:5001 \
  -e ControlPlane__SharedSecret=<secret> \
  -e Agent__Id=home-1 -e Agent__Name=home-1 \
  -v min-go-agent-data:/app/data \
  --restart unless-stopped \
  ghcr.io/fffirer/reverseproxy-dp:latest
```

3. 实例自动出现在控制台「实例管理」页，可查看运行时配置、下发重载/关停。

## 7. 接入通道：数据面节点 + Cloudflare 隧道

对标 Portainer 的多环境管理，把「数据面节点」做成可管理的持久资产，用于按地区/机房划分数据面；Cloudflare 隧道继续作为外部接入通道并列存在（隧道本身不落库，经 Cloudflare API 管理）。

- **节点资产化**：`DataPlaneNodes` 与 `InstanceNodeAssignments` 两张表落库（迁移 `DataPlaneNodes`，由分组表就地改名而来）。启动时幂等确保一个默认节点（Id=`default`，名「默认节点」，不可删除、可重命名），存量 `local` 路由迁移改写为 `node:default`。节点在「接入通道」页维护（新建/重命名/删除），删除被路由引用的节点返回 409 并附引用路由清单（fail-closed，不级联）。
- **双向维护**：实例与节点的绑定既可在「接入通道」页节点卡片管理，也可在「实例管理」页行内下拉直接绑定（`PUT /api/instances/{id}/node`，`null` 解绑）。绑定立即生效：更新内存态与连接节点，并向该实例重推配置快照。
- **未绑定不推送（fail-closed）**：未绑定任何节点的实例连接后不发送初始化快照、广播时被跳过、解绑时推送空快照（无路由、无证书）清除残留规则——实例不生效任何转发规则，直到绑定节点。连接保持存活（心跳/命令通道可用）。
- **隔离语义**：路由绑定 `node:<NodeId>` 时，只有该节点的数据面收到对应规则；绑定 Cloudflare 隧道的路由对所有已绑定节点的数据面广播（隧道 ingress 一律回源到数据面，规则必须装在数据面）。
- **证书随节点过滤**：快照按连接所属节点组装，证书只下发该节点路由引用的域名（与数据面 SNI 通配匹配同规则），私钥不跨节点；any-host 路由（空域名）回退全量下发；空快照对应空证书。

迁移注意：节点引用按 Id 而非名称，重命名安全；老数据面无需升级（过滤全在控制面侧，proto 零改动）。

## 8. 配置键一览

| 组件 | 键 | 默认 | 说明 |
| --- | --- | --- | --- |
| CP | `DataPlane:SharedSecrets` | 空 | 空=不校验；非空=强制校验所有数据面 gRPC 连接 |
| CP | `Kestrel:Endpoints:gRPC:Url` | `http://+:5001` | 数据面接入端点 |
| DP | `ControlPlane:GrpcUrl` | `http://localhost:5001` | 控制面 gRPC 地址 |
| DP | `ControlPlane:SharedSecret` | 空 | 与 CP `SharedSecrets` 配套 |
| DP | `Agent:Id` | 空 | 固定身份；空则用状态文件 |
| DP | `Agent:Name` | 空 | 控制台显示名；空则用 `Agent:Id` |
| DP | `Agent:Version` | 程序集版本 | 覆盖上报版本 |
| DP | `Agent:StateFile` | `/app/data/agent-id` | 身份持久化文件（需挂卷） |

## 9. 决策记录

- 实例注册表**不落库**（内存化）：实例是运行时状态而非配置资产，符合"配置优先于运行时状态"的边界；重启即失、重连即恢复，spec `in-memory-instance-store` 记录此决策。
- 加入凭据走**配置文件**而非 UI 管理页：基建类资源按环境配置表达，不做 CRUD 页面。
- 不重命名组件：CP/DP 命名保留，文档以 Server/Agent 类比说明关系模型。
