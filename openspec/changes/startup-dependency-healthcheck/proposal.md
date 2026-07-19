## Why

Control plane 在 docker-compose 启动时仅等容器创建（`depends_on: control-plane`），不等服务就绪。数据面启动后立即尝试 gRPC 连接，遇到连接错误日志、重试噪声。当前虽然代码层面有 `WaitForInitialConfigAsync(30s)` 和重试退避机制兜底，但缺乏容器编排层的就绪保障，启动体验不干净。

引入 Microsoft 官方 ASP.NET Core Health Checks，在三层层面解决就绪门问题：Docker 运行时、Compose 编排、应用层。

## What Changes

- ControlPlane 新增 `/healthz/live`（存活）和 `/healthz/ready`（就绪）两个健康检查端点
- ControlPlane.Dockerfile runtime stage 安装 `curl`，添加 `HEALTHCHECK` 指令
- docker-compose.yml 中 control-plane 添加 `healthcheck:` 配置（探测 `/healthz/ready`）
- docker-compose.yml 中 data-plane 的 `depends_on` 从默认启动升级为 `condition: service_healthy`
- DataPlane 新增 `/healthz/live` 存活端点
- DataPlane `WaitForInitialConfigAsync` 超时从 30s 延长到 60s
- docker-compose.local.yml 同步以上变更（保留 `restart: "no"` 策略）
- 引入 NuGet 包 `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.0

## Capabilities

### New Capabilities

- `container-healthchecks`: 容器健康检查端点、Docker HEALTHCHECK 指令、Compose healthcheck 配置的完整实现

### Modified Capabilities

- `local-verify-infra`: docker-compose.local.yml 需增加 healthcheck 配置，与 production compose 同步。现有 requirement "Containers SHALL NOT auto-restart on failure" 保持不变

## Impact

- `src/MinGo.ControlPlane.Api/Program.cs` — 注册 health check 服务 + 映射两个端点
- `src/MinGo.ControlPlane.Api/MinGo.ControlPlane.Api.csproj` — 新增 EF Core health check NuGet 包
- `Directory.Packages.props` — 新增包版本条目
- `ControlPlane.Dockerfile` — runtime stage +`curl` 安装 + `HEALTHCHECK` 指令
- `src/MinGo.DataPlane/Program.cs` — 新增 `/healthz/live` 端点
- `src/MinGo.DataPlane/ConfigSync/ConfigSyncService.cs` — WaitForInitialConfigAsync 超时 30s→60s
- `docker-compose.yml` — CP 健康检查 + DP depends_on 升级
- `docker-compose.local.yml` — 同上
