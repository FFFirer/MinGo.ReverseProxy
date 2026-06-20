## Context

MinGo.ReverseProxy 是控制面+数据面分离的反向代理系统，通过 docker-compose 编排。当前数据面启动时 `depends_on: control-plane` 仅等容器启动，不等服务就绪。数据面 `ConfigSyncService` 有重试退避机制（1s/2s/5s/10s/30s）和 30s 启动超时，能正确处理启动时序问题，但启动期间会产生大量连接错误日志，启动体验不理想。

数据面启动流程：
1. 创建 gRPC 客户端指向 `http://control-plane:5001`
2. `ConfigSyncService.StartAsync()` 启动 `RunSyncLoopAsync` 后台任务
3. `WaitForInitialConfigAsync(30s)` 阻塞主线程等待首次配置
4. `RunSyncLoopAsync` 内部重试退避循环处理连接失败

控制面启动流程：
1. EF Core 迁移（通过 `migrate` 一次性服务执行）
2. Kestrel 启动，监听 5000（HTTP/REST + 前端）和 5001（gRPC）
3. 当前无健康检查端点

## Goals / Non-Goals

**Goals:**
- 在容器编排层（Compose）建立明确的启动顺序：控制面就绪后数据面才启动
- 使用微软官方 ASP.NET Core Health Checks，不引入社区第三方包
- 三层就绪门：Docker 运行时（liveness）→ Compose 编排（readiness）→ 应用层（业务就绪）
- 控制面健康端点同时服务于 K8s readiness probe（未来迁移场景）

**Non-Goals:**
- 不修改现有的 gRPC 重试退避逻辑
- 不引入 `dockerize`/`wait-for-it.sh`/sidecar 等外部依赖
- 不改变 DataPlane 的 `restart: "no"` 策略（local compose）
- 不为 DataPlane 添加 readiness probe（当前无下游依赖方）
- 不实现 gRPC Health Checking Protocol（HTTP 端点已足够）

## Decisions

### 决策 1：健康检查使用 ASP.NET Core 官方 `AddHealthChecks()` + `AddDbContextCheck<TContext>()`

**选择**：`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` NuGet 包

**理由**：
- `AddHealthChecks()` 和 `MapHealthChecks()` 是 ASP.NET Core 共享框架的一部分，无需额外 NuGet
- `AddDbContextCheck<AppDbContext>()` 来自 EF Core 专用的 health check 包，版本跟随 .NET 运行时（10.0.0）
- 比手动实现 `/healthz` 端点 + `db.Database.CanConnectAsync()` 更标准，支持 tags、结果状态码配置

**替代方案**：
- 手动 `/healthz` + `CanConnectAsync()`：可行但不够标准化，无法利用 tags 过滤
- 社区 `AspNetCore.HealthChecks.Sqlite`：增加额外依赖，且微软官方包已覆盖此场景

### 决策 2：双端点设计 `/healthz/live` + `/healthz/ready`

**选择**：
- `/healthz/live`：`Predicate = _ => false`，不运行任何检查，仅返回 200，用于 Docker HEALTHCHECK
- `/healthz/ready`：`Predicate = check => check.Tags.Contains("ready")`，仅运行 tagged 检查（EF Core DbContext），用于 Compose healthcheck

**理由**：
- Docker HEALTHCHECK 应只验证进程存活（liveness），避免依赖外部资源导致误杀
- Compose healthcheck 应验证服务就绪（readiness），包括数据库连接性
- 与 K8s 的 liveness/readiness probe 概念对齐，未来迁移时直接复用

**替代方案**：
- 单一 `/healthz` 端点：无法区分 liveness 和 readiness，Docker HEALTHCHECK 可能因 DB 瞬断误杀容器

### 决策 3：Docker HEALTHCHECK 使用 `/healthz/live`，Compose healthcheck 使用 `/healthz/ready`

**选择**：
- Dockerfile `HEALTHCHECK CMD curl -fsS http://localhost:5000/healthz/live`
- docker-compose `healthcheck: test: curl -f http://localhost:5000/healthz/ready`

**理由**：
- Dockerfile HEALTHCHECK 由 Docker 守护进程执行，决定是否重启容器。应轻量，不依赖 DB
- Compose healthcheck 由 docker-compose 执行，决定服务间启动顺序。应验证 DB 就绪
- 两层使用不同端点，职责分离

### 决策 4：DataPlane 保留 `WaitForInitialConfigAsync`，超时从 30s 延长到 60s

**选择**：保持应用层等待，超时 60s

**理由**：
- Compose healthcheck 仅确保控制面就绪（L3），不保证配置已推送到数据面（L4）
- `WaitForInitialConfigAsync` 是最终业务就绪门：超时后 proxy 运行但返回 503，后台重试继续
- 60s 超时匹配 Compose healthcheck 的最坏情况启动时间（`start_period(15s) + interval(15s) × retries(2) = 45s`）

### 决策 5：DataPlane 添加 `/healthz/live` 存活端点

**选择**：DataPlane 也添加 `/healthz/live`（无依赖检查）

**理由**：
- Docker HEALTHCHECK 可用于 DataPlane，当进程异常时触发容器重启
- 不添加 readiness 端点：当前无下游服务依赖 DataPlane 就绪状态

## Risks / Trade-offs

### [风险] Compose healthcheck 延迟导致启动时间增加
**缓解**：`start_period=15s` 给控制面充足启动时间，`retries=2` 允许 2 次瞬断。总最坏情况 45s，与数据面 60s 超时对齐。

### [风险] 健康检查端点增加攻击面
**缓解**：`/healthz/live` 和 `/healthz/ready` 不返回敏感信息，仅返回状态码和检查结果。生产环境可考虑限制访问（如仅内网可达）。

### [风险] `restart: "no"` 下 Compose healthcheck 无效
**缓解**：local compose 中 `restart: "no"` 意味着容器退出后不重启。这是有意设计（可见失败），不依赖 Docker HEALTHCHECK 重启机制。生产 compose 使用 `restart: unless-stopped`。

### [Trade-off] 引入 curl 依赖增加镜像体积
**接受**：curl 仅约 100KB，对基础镜像影响可忽略。这是 Docker HEALTHCHECK 的标准做法。

### [Trade-off] 健康检查端点增加请求开销
**接受**：健康检查仅在探针周期执行（15s 间隔），开销极小。生产环境可调整间隔或限制来源。
