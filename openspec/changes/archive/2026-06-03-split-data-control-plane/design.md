## Context

当前 MinGo.ReverseProxy 是单一进程架构，通过两个端口组（AdminPorts/ProxyPorts）在同一个 Kestrel 宿主中同时运行控制面（Blazor Server + 6 个 API Controllers + Auth）和数据面（YARP 反向代理）。两者共享两个 SQLite DbContext（ApiDbContext 和 ApplicationDbContext），配置变更通过进程内 MemoryMessageNotificationService 事件总线传递。这种架构导致：无法独立扩缩容、数据面故障会影响管理能力、前端被锁定在 Blazor Server（SignalR 长连接）模式下。

本项目已有一个 `src/MinGo.ReverseProxy` 包含所有功能、`src/MinGo.Shared` 包含冗余模型、`src/MinGo.Core` 和 `src/MinGo.Application` 和 `src/MinGo.Infrastructure` 按整洁架构组织但尚未完全分离。现有 Dockerfile 使用多阶段构建将前端（Vite + TailwindCSS v4）和后端打包在同一镜像中。

### 当前项目依赖

```
ReverseProxy → Application → Core
ReverseProxy → Infrastructure → Core  
ReverseProxy → Shared (冗余)
Application → Core
Infrastructure → Core
Shared (独立，模型与 Core 重叠)
```

### 当前运行时

```
同一进程:
  Admin Ports (如 6139):
    - Blazor Server Pages (Dashboard, Routes, Clusters, etc.)
    - API Controllers (ApiManagement, Certificates, Monitoring, Logs, Telemetry, Instances)
    - AuthService (Identity Cookie)
    - Static Files (Vite 构建的 CSS)
  Proxy Ports (如 8080):
    - YARP Reverse Proxy
    - GatewayTelemetryMiddleware
    - CertificateSelector (SNI)
```

## Goals / Non-Goals

**Goals:**
- 将数据面和控制面拆分为独立的进程，通过 gRPC 双向流通信
- 数据面不直接访问数据库，只通过控制面接口获取配置
- 控制面保留现有 API Controllers 和 EF Core + SQLite，新增 gRPC 服务端
- 前端从 Blazor Server 迁移为 SolidJS + TailwindCSS v4 SPA，严格参照原型设计
- 前端认证保持 Cookie 方案（跨域适配）
- 提供 3 个独立 Dockerfile，docker-compose 编排
- 保留 `MinGo.Core`、`MinGo.Application`、`MinGo.Infrastructure` 不变
- 废弃 `MinGo.ReverseProxy` 和 `MinGo.Shared`

**Non-Goals:**
- 不修改现有数据表结构（保留 ApiDbContext 和 ApplicationDbContext 的 schema）
- 不替换 YARP 反向代理核心
- 不涉及生产部署 / Kubernetes 编排（仅 Docker Compose 本地运行）
- 不实现新的业务功能（仅架构拆分）
- 不涉及监控系统（Prometheus/Grafana）的集成

## Decisions

### D1: gRPC 双向流作为数据面与控制面通信协议

**选择**: gRPC 双向流 (`ConfigReplication`, `HeartbeatCollect`, `EventSubscription`)

**理由**:
- YARP 配置变更需要实时推送，双向流可持续推送配置增量，避免轮询延迟
- 心跳上报是高频率小数据包场景，gRPC 双向流可复用单条长连接
- .NET 对 gRPC 有原生支持（`Microsoft.AspNetCore.Grpc`），与 ASP.NET Core 深度集成
- 相比 WebSocket / SignalR：gRPC 有强类型契约（protobuf）、原生流式语义、更好的性能

**备选方案**:
- *SignalR*: 如果只需要简单推送，但缺少流式控制和强类型契约
- *REST 轮询*: 简单但延迟大、无效请求多，不适用于高频指标上报
- *RabbitMQ/Kafka*: 引入了太重的外部依赖

### D2: 控制面保持 SQLite + EF Core，不引入新数据库

**选择**: 控制面继续使用 SQLite（与当前相同）

**理由**:
- SQLite 对单进程场景足够，控制面是唯一写入的进程
- 保留现有 Migration，不需要数据迁移
- 数据面不访问数据库，不存在并发写入问题

### D3: 前端使用 SolidJS + TailwindCSS v4 + Vite

**选择**: SolidJS 构建 SPA，TailwindCSS v4 CSS-first 配置

**理由**:
- SolidJS 编译型响应式框架，无虚拟 DOM，体积小、性能好
- TailwindCSS v4 已存在于项目中（`package.json` 已有 `tailwindcss: ^4.1.18`）
- Vite 开发体验优秀，支持 HMR
- 原型设计明确使用 TailwindCSS + Font Awesome

### D4: 前端认证维持 Cookie 方案

**选择**: ASP.NET Core Identity Cookie + CORS AllowCredentials

**理由**:
- 现有 `AuthService` 基于 Cookie，复用已有身份认证逻辑
- 无需引入 JWT 的 token 刷新/存储/过期管理复杂性
- `SameSite=Lax` + `credentials: 'include'` 可安全跨域
- Cookie 对 Blazor Server 到 SPA 的迁移改动最小

### D5: 证书通过控制面 API 传输而非 gRPC

**选择**: 证书二进制数据嵌入 ConfigSnapshot 的 gRPC 消息中（`bytes certificate_data`）

**理由**:
- 证书数据已经在数据库中作为 `byte[]` 存储
- gRPC 消息支持 bytes 类型，传输效率高
- 省去数据面额外发 HTTP 请求拉取证书的步骤
- 配置更新时一并将证书推送到数据面

### D6: 三个独立 Dockerfile + docker-compose 编排

**选择**: DataPlane / ControlPlane / Frontend 各一个 Dockerfile，docker-compose 三服务

**理由**:
- 每个组件可独立构建和发布
- docker-compose 提供本地开发和测试的便利
- 前端通过 Nginx 容器提供静态文件服务，反向代理到控制面 API
- 控制面暴露两个端口：5000（HTTP REST API）、5001（gRPC）

### D7: 废弃 MinGo.Shared 项目

**选择**: 将其模型合并到 MinGo.Core，DbContext 逻辑合并到 MinGo.Infrastructure

**理由**:
- `MinGo.Shared` 的 `ProxyDbContext`、`GatewayConfig` 等与 `MinGo.Core/Infrastructure` 重叠
- 整洁架构中 Shared 层应作为 NuGet 包或 Core 的一部分，不应单独项目
- 减少项目数，简化依赖关系

## Risks / Trade-offs

| 风险 | 缓解措施 |
|------|---------|
| gRPC 双向流连接中断导致数据面配置过期 | 数据面 ConfigSyncService 实现重试+指数退避，控制面 gRPC 服务端检测断连后标记实例离线 |
| 控制面与数据面版本不兼容（protobuf schema 变更） | protobuf 字段使用 `optional` / 向后兼容的编号规则，新增字段不破坏旧版 |
| 证书二进制数据通过 gRPC 传输增大消息体积 | 证书在数据面缓存，控制面仅在证书变更时推送，非每次配置更新都传输 |
| Cookie 跨域安全限制 | SameSite=Lax 兼容跨域 POST 但不兼容第三方上下文的严格模式，前端和 API 同域名部署时可省略 CORS |
| 迁移期间需要同时维护新旧两个项目 | 建议阶段性迁移：先新建项目并行运行，验证功能后删除旧项目 |
| SQLite 不支持并发写入 | 拆分后只有控制面写入 SQLite，数据面只读不写，不存在并发问题 |

## 系统架构图

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Docker Compose                               │
│                                                                     │
│  ┌─────────────────────┐    ┌──────────────────────────────────┐    │
│  │  Frontend            │    │  ControlPlane                    │    │
│  │  (Nginx + SolidJS)   │    │  (.NET 10 + ASP.NET Core)        │    │
│  │                      │    │                                  │    │
│  │  ┌───────────────┐   │    │  REST API (Controllers) ←────────┼──┐ │
│  │  │ Static Files  │   │    │  :5000                           │  │ │
│  │  │ (index.html,  │   │    │                                  │  │ │
│  │  │  .js, .css)   │   │    │  gRPC Server ←──────────────────┼──┼─┼──┐
│  │  └───────────────┘   │    │  :5001 (ConfigReplication,       │  │ │  │
│  │                      │    │        HeartbeatCollect,         │  │ │  │
│  │  :80                  │    │        EventSubscription)        │  │ │  │
│  └──────────────────────┘    │                                  │  │ │  │
│                              │  Auth (Identity + Cookie)         │  │ │  │
│                              │                                  │  │ │  │
│                              │  EF Core → SQLite                 │  │ │  │
│                              │  (ApiDbContext + AppDbContext)     │  │ │  │
│                              └──────────────────────────────────┘  │ │  │
│                                                                   │ │  │
│                              ┌──────────────────────────────┐     │ │  │
│                              │  DataPlane                    │◄────┘ │  │
│                              │  (.NET 10 + YARP)             │       │  │
│                              │                               │       │  │
│                              │  YARP Proxy ──── 后端服务      │       │  │
│                              │  :8080                         │       │  │
│                              │                               │       │  │
│                              │  gRPC Client (ConfigSync,      │◄──────┘  │
│                              │    Heartbeat, Event)           │          │
│                              │                               │          │
│                              │  TelemetryStore (内存)          │          │
│                              │  CertificateManager (从CP拉)    │          │
│                              └──────────────────────────────┘          │
└───────────────────────────────────────────────────────────────────────┘
```

## gRPC 服务设计

### ConfigReplication (配置同步 - 双向流)

```
数据面(客户端)  ─── ConfigSubscription ──▶  控制面(服务端)
                    包含版本号
                  
                ◀── ConfigSnapshot ─────────  全量初始化
                
                ◀── ConfigSnapshot ─────────  配置变更推送
                    (增量/全量)
```

### HeartbeatCollect (心跳收集 - 双向流)

```
数据面  ─── HeartbeatRequest ──▶  控制面
           (cpu/mem/请求统计)
           
        ◀── HeartbeatResponse ──  确认 + 控制指令
             (ReloadConfig/ReloadCerts)
```

### EventSubscription (事件订阅 - 双向流)

```
数据面  ─── EventStream ──▶  控制面  (实例事件上报)
        ◀── EventStream ───  (控制面推送告警/通知)
```

## 前端页面映射

| 原型页面 | SolidJS 路由 | 关联 API |
|---------|-------------|---------|
| 仪表盘 | `/` | `GET /api/monitoring/metrics`, `/requests`, `/services` |
| 路由管理 | `/routes` | `GET/POST/PUT/DELETE /api/apimanagement/routes/{id}` |
| 集群管理 | `/clusters` | `GET/POST/PUT/DELETE /api/apimanagement/clusters/{id}` |
| 安全管理 | `/security` | (后续实现) |
| 监控 | `/monitoring` | `GET /api/monitoring/requests?start=&end=` |
| 日志管理 | `/logs` | `GET /api/logs/access`, `/errors`, `/statistics` |
| 实例管理 | `/instances` | `GET /api/instances` |
| 证书管理 | `/certificates` | `GET/POST/PUT/DELETE /api/certificates/{id}` |
| 设置 | `/settings` | (全局配置) |
| 登录 | `/login` | `POST /api/auth/login` |
| 注册 | `/register` | `POST /api/auth/register` |

## 数据流示例：创建路由

```
1. User 在 SolidJS Routes 页面填写表单
2. SolidJS → POST /api/apimanagement/routes (with Cookie)
3. ControlPlane ApiManagementController.CreateRoute()
4.   → ApiManagementService.CreateRouteAsync() 
5.     → ApiDbService.CreateRouteAsync() → EF Core → SQLite
6.     → ConfigReplicationService.BroadcastConfigUpdateAsync()
7.       → gRPC Server 向所有已连接 DataPlane 推送 ConfigSnapshot
8. DataPlane ConfigSyncService 接收 ConfigSnapshot
9.   → DataPlaneConfigProvider.ApplyConfig() → 更新内存配置
10.  → YARP 检测 ChangeToken → 热重载路由配置
11. SolidJS 收到 201 Created → 刷新路由列表
```

## 容器端口规划

| 服务 | 容器端口 | 协议 | 说明 |
|------|---------|------|------|
| ControlPlane | 5000 | HTTP | REST API (前端调用) |
| ControlPlane | 5001 | HTTP/2 | gRPC (数据面连接) |
| DataPlane | 8080 | HTTP | 反向代理入口 |
| Frontend | 80 | HTTP | SPA 静态文件 |

## 迁移步骤

1. **阶段一：创建新项目结构**
   - 新建 `MinGo.DataPlane` 项目
   - 新建 `MinGo.ControlPlane.Api` 项目  
   - 新建 `frontend/min-go-console/` SolidJS 项目

2. **阶段二：实现 gRPC 协议**
   - 定义 `protos/dataplane.proto`
   - 实现控制面 gRPC 服务端
   - 实现数据面 gRPC 客户端

3. **阶段三：实现控制面 API**
   - 从原项目复制 Controllers 到 ControlPlane.Api
   - 配置 CORS + Cookie 认证
   - 添加 gRPC 服务注册

4. **阶段四：实现数据面功能**
   - 实现 DataPlaneConfigProvider（替代 DatabaseProxyConfigProvider）
   - 实现 ConfigSyncService（gRPC 配置同步）
   - 实现 HeartbeatReporter（gRPC 心跳上报）
   - 实现 CertificateManager（从 API 拉取）
   - 保留 YARP + TelemetryMiddleware

5. **阶段五：实现 SolidJS 前端**
   - 参照原型实现所有页面
   - 集成 API 客户端（Cookie 认证）
   - 深色模式、响应式布局

6. **阶段六：容器化**
   - 编写 3 个 Dockerfile
   - 更新 docker-compose.yml
   - 端到端测试

7. **阶段七：清理**
   - 删除 `MinGo.ReverseProxy` 项目
   - 删除 `MinGo.Shared` 项目
   - 删除 Blazor 相关文件
   - 更新 `.slnx`
