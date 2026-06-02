## 1. 项目结构准备

- [x] 1.1 创建 `MinGo.DataPlane` ASP.NET Core Web 项目，配置 YARP 和 gRPC 客户端依赖
- [x] 1.2 创建 `MinGo.ControlPlane.Api` ASP.NET Core Web 项目，配置 Controllers 和 gRPC 服务端依赖
- [x] 1.3 创建 `frontend/min-go-console/` 目录，初始化 SolidJS + TailwindCSS v4 + Vite + TypeScript 项目
- [x] 1.4 更新 `MinGo.ReverseProxy.slnx` 添加新项目引用，标记 `MinGo.ReverseProxy` 和 `MinGo.Shared` 为待移除
- [x] 1.5 更新 `Directory.Packages.props` 添加 gRPC 相关 NuGet 包版本（Grpc.AspNetCore, Google.Protobuf, Grpc.Tools）

## 2. gRPC 协议定义

- [x] 2.1 创建 `protos/dataplane.proto` 定义 `ConfigReplication` 服务（ReplicateConfig 双向流 RPC + ConfigSubscription/ConfigSnapshot 消息）
- [x] 2.2 在 proto 中定义 `HeartbeatCollect` 服务（ReportHeartbeat 双向流 RPC + HeartbeatRequest/HeartbeatResponse/ControlCommand 消息）
- [x] 2.3 在 proto 中定义 `EventSubscription` 服务（SubscribeEvents 双向流 RPC + EventStream/EventType 消息）
- [x] 2.4 在 `MinGo.ControlPlane.Api.csproj` 和 `MinGo.DataPlane.csproj` 中配置 `Protobuf` ItemGroup 自动生成 C# 代码
- [x] 2.5 在 `MinGo.Core` 中创建共享类型映射（领域模型已存在且与 protobuf 字段对应，通过映射方法转换）

## 3. 控制面 gRPC 服务端实现

- [x] 3.1 实现 `ConfigReplicationService`（管理数据面订阅连接、配置变更时广播推送）
- [x] 3.2 实现 `HeartbeatCollectService`（接收数据面心跳、更新实例状态、下发控制指令）
- [x] 3.3 实现 `EventSubscriptionService`（双向事件路由、持久化事件到 GatewayEvents 表）
- [x] 3.4 实现 `DataPlaneConnectionManager`（跟踪所有已连接数据面实例、健康状态）

## 4. 数据面核心实现

- [x] 4.1 实现 `ConfigSyncService`（gRPC 客户端：连接/订阅/重试/应用 ConfigSnapshot）
- [x] 4.2 实现 `DataPlaneConfigProvider`（实现 `IProxyConfigProvider`，从 ConfigSnapshot 构建 YARP 配置，支持热重载）
- [x] 4.3 实现 `HeartbeatReporter`（BackgroundService：定时采集系统指标和 TelemetryStore 数据，通过 gRPC 上报）
- [x] 4.4 实现 `CertificateManager`（从 ConfigSnapshot 的证书字段加载到内存缓存，支持重新加载指令，集成在 Kestrel/CertificateExtensions.cs 的 DataPlaneCertificateSelector 中）
- [x] 4.5 从原项目迁移 `GatewayTelemetryMiddleware` 和 `TelemetryStore`
- [x] 4.6 从原项目迁移 Kestrel 配置（KestrelOptionsSetup, IServerCertificateSelector, Extensions）

## 5. 控制面 API 迁移

- [x] 5.1 从 `MinGo.ReverseProxy` 复制所有 6 个 Controllers 到 `MinGo.ControlPlane.Api/Controllers/` 并调整命名空间，新增 AuthController
- [x] 5.2 创建 `AuthController` 并使用 Cookie 认证（CORS AllowCredentials + SameSite=Lax）
- [x] 5.3 配置 `Program.cs`：注册 EF Core DbContext、Identity、Controllers、CORS、gRPC 服务
- [x] 5.4 配置 Kestrel 双端口（5000 HTTP REST + 5001 HTTP/2 gRPC）
- [x] 5.5 创建 `ConfigUpdateGrpcBroadcaster` 监听配置变更事件并通过 `ConfigReplicationService.BroadcastConfigUpdateAsync()` 推送

## 6. SolidJS 前端实现

- [x] 6.1 配置 Vite + TailwindCSS v4 + TypeScript，实现 `index.html` 入口
- [x] 6.2 实现布局组件：`MainLayout`（Header + Sidebar + ContentArea）、`Header`（搜索/主题/通知/用户）、`Sidebar`（8个导航项/响应式折叠）
- [x] 6.3 实现通用 UI 组件（Card/Badge/Button/Input 样式在 app.css 中统一定义，Modal 在 Routes 页面中实现为 RouteFormModal）
- [x] 6.4 实现 `api/client.ts` 封装（`credentials: 'include'` + 401 拦截 + 错误处理）
- [x] 6.5 实现认证页面：`Login.tsx`
- [x] 6.6 实现 `Dashboard.tsx`（4 个 StatCard + 请求趋势 + 服务状态 + 概览）
- [x] 6.7 实现 `Routes.tsx`（表格 CRUD + 搜索/筛选 + 创建/编辑弹窗）
- [x] 6.8 实现 `Clusters.tsx`（卡片网格 + 目标管理 + 删除）
- [x] 6.9 实现 `Certificates.tsx`（表格 + 状态）
- [x] 6.10 实现 `Monitoring.tsx`（图表占位 + 告警记录）
- [x] 6.11 实现 `Logs.tsx`（日志流式查看器 + 搜索/查询）
- [x] 6.12 实现 `Instances.tsx`（统计 + 表格）
- [x] 6.13 实现 `Settings.tsx`（基本设置/高级设置表单）
- [x] 6.14 实现 `Security.tsx`（API 密钥管理 + IP 控制 + 速率限制 UI）
- [x] 6.15 实现深色模式（localStorage 持久化 + TailwindCSS dark: 类，在 theme.ts 中实现）
- [x] 6.16 配置前端路由（solid-router）和页面过渡动画

## 7. 容器镜像打包

- [x] 7.1 编写 `DataPlane.Dockerfile`（dotnet-sdk 构建 → aspnet 运行时，暴露 8080）
- [x] 7.2 编写 `ControlPlane.Dockerfile`（dotnet-sdk 构建 + ef migrations bundle → aspnet 运行时，暴露 5000/5001）
- [x] 7.3 编写 `frontend/min-go-console/Dockerfile`（node 构建 → nginx 运行时）
- [x] 7.4 更新 `docker-compose.yml` 定义三个服务 + 持久化卷 + depends_on

## 8. 清理与验证

- [ ] 8.1 端到端验证：`docker compose up` 启动三个服务，确认数据面从控制面获取配置、心跳上报成功（需要 Docker 环境）
- [ ] 8.2 验证前端：`pnpm dev` 启动开发服务器，确认所有页面渲染正确、Cookie 认证正常、深色模式切换正常
- [ ] 8.3 删除 `src/MinGo.ReverseProxy` 项目（待 docker-compose 端到端验证后执行）
- [ ] 8.4 删除 `src/MinGo.Shared` 项目（待模型合并至 Core 后执行）
- [x] 8.5 删除 Blazor 相关文件（.razor、.cshtml、Blazor Server 配置、旧 wwwroot js/css）
- [x] 8.6 更新 `.slnx` 添加新项目引用（ReverseProxy 和 Shared 已注释待移除）
- [x] 8.7 验证构建通过：`dotnet build` 无错误 + `pnpm build` 无错误
