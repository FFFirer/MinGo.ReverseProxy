## Why

当前 MinGo.ReverseProxy 是一个单体进程，数据面（YARP 反向代理）和控制面（API + Blazor UI + 认证）运行在同一进程中，共享同一数据库。这导致：数据面故障会影响控制面可用性，两者无法独立扩缩容，配置变更响应路径复杂（进程内内存事件），且当前 Blazor Server UI 无法与现代前端工作流（SPA 独立部署）兼容。拆分为独立的数据面和控制面后，两者可独立部署、独立扩缩容，通过 gRPC 双向流实现高效的配置下发与心跳上报。

## What Changes

- **BREAKING**: 新建 `MinGo.DataPlane` 项目，从 `MinGo.ReverseProxy` 剥离 YARP 代理、遥测、证书管理功能，**不直接访问数据库**，只通过 gRPC 与控制面交互
- **BREAKING**: 新建 `MinGo.ControlPlane.Api` 项目，从 `MinGo.ReverseProxy` 剥离所有 API Controllers，保留 EF Core + SQLite（唯一数据库写进程），新增 gRPC 服务端
- **BREAKING**: 废弃 `MinGo.ReverseProxy` 项目（所有 Blazor Pages 被替换）
- **BREAKING**: 废弃 `MinGo.Shared` 项目（模型整合到 Core）
- **NEW**: 定义 `protos/dataplane.proto` 共享 gRPC 协议（ConfigReplication / HeartbeatCollect / EventSubscription 双向流）
- **NEW**: 新建 `frontend/min-go-console/` SolidJS + TailwindCSS v4 前端项目，严格参照 `docs/control-plane-prototype.html` 原型设计
- 保留 `MinGo.Core`、`MinGo.Application`、`MinGo.Infrastructure` 不变
- 控制面认证保持 ASP.NET Core Identity Cookie 方案，适配前端跨域（CORS AllowCredentials）
- 新增 3 个 Dockerfile 独立容器镜像：DataPlane / ControlPlane / Frontend

## Capabilities

### New Capabilities

- `grpc-config-replication`: 控制面通过 gRPC 双向流向数据面推送配置更新（路由、集群、证书），数据面保持长连接实时接收
- `grpc-heartbeat`: 数据面通过 gRPC 双向流定期上报心跳、系统指标、请求统计数据到控制面
- `grpc-event-subscription`: 控制面与数据面之间的双向事件订阅通道，用于通知、告警等控制指令
- `solidjs-frontend`: 基于 SolidJS + TailwindCSS v4 的 SPA 前端，包含 Dashboard/Routes/Clusters/Security/Monitoring/Logs/Instances/Certificates/Settings 等管理页面
- `data-plane-isolation`: 数据面独立进程运行，不直接访问数据库，所有配置来自控制面 gRPC 接口

### Modified Capabilities

<!-- 无现有 spec 需要修改，这是首次定义能力 -->

## Impact

- **代码结构**: 5 个项目重构为 5+1（Core/Application/Infrastructure 保留 + DataPlane 新建 + ControlPlane.Api 新建 + Frontend 新建），ReverseProxy 和 Shared 废弃
- **通信方式**: 数据面与控制面之间从进程内事件（MemoryMessageNotificationService）改为 gRPC 双向流网络通信
- **数据库**: 只有控制面进程访问 SQLite，数据面完全不再直连数据库
- **前端**: 从 Blazor Server SSR 改为 SolidJS SPA + REST API，部署方式从后端渲染变为独立 Nginx 容器
- **配置下发**: 从 `ApiManagementService → GatewayEventService → MemoryMessageNotificationService → ConfigUpdateEventListener → DatabaseProxyConfigProvider.RefreshAsync()`（进程内）变为 `ControlPlane gRPC Server → DataPlane gRPC Client → DataPlaneConfigProvider`（网络双向流）
- **容器化**: 从单一 Dockerfile 变为三个独立的 Dockerfile + docker-compose 三服务编排
