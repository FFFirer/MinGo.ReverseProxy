## Context

MinGo.ReverseProxy 已完成从单体到微服务架构的拆分，包含三个独立部署组件：
- **ControlPlane.Api** — ASP.NET Core Web API + gRPC 服务端 + EF Core
- **DataPlane** — ASP.NET Core + YARP 反向代理 + gRPC 客户端
- **frontend/min-go-console** — Vite SPA，nginx 托管

但 CI/CD 仍停留在旧架构：Gitea Actions 构建的是已废弃的单体镜像（`Dockerfile`），Compose 文件使用本地构建（`build: .`）而非 registry 镜像。需要适配新架构并支持 Portainer Stack 部署。

## Goals / Non-Goals

**Goals:**
- Gitea Actions matrix 并行构建 3 个独立容器镜像并推送到私有 registry
- docker-compose.yml 改为使用 registry 镜像，可由 Portainer Stack 直接导入
- 保留 migrate 服务的 efbundle one-shot 迁移模式
- 保持开发环境可本地 `docker compose up` 构建运行

**Non-Goals:**
- 不修改 Dockerfiles 内容（ControlPlane.DataPlane、frontend 的 Dockerfile 保持不变）
- 不涉及 Portainer API 自动化（CD 手动触发）
- 不涉及监控、日志、告警等运维附加功能
- 暂不接入 `bridge-local` 外部网络

## Decisions

### 1. Matrix 构建策略

**选择**：Gitea Actions 使用 `strategy/matrix` 并行构建 3 个镜像。

**理由**：
- 3 个镜像解耦，各自独立构建，失败不影响其他
- 比顺序构建快 3x
- 比单一 Dockerfile 多阶段构建更清晰

**Matrix 配置**：
```yaml
strategy:
  matrix:
    component:
      - name: cp
        dockerfile: ControlPlane.Dockerfile
        image: reverseproxy-cp
      - name: dp
        dockerfile: DataPlane.Dockerfile
        image: reverseproxy-dp
      - name: fe
        dockerfile: frontend/min-go-console/Dockerfile
        image: reverseproxy-fe
```

### 2. Registry 与版本标签

**镜像命名**：`registry.private.fffirer.top:9081/1mingo/reverseproxy-{cp,dp,fe}`

**标签策略**：沿用 CertManager 模式
- **Release 触发**：`{tag_name}`（如 `1.0.8`）+ `latest`
- **workflow_dispatch 触发**：`dev-{sha:7}-{timestamp}` + `latest`

### 3. docker-compose.yml 替换方案

| 变更 | 原值 | 新值 |
|------|------|------|
| 构建方式 | `build: .` | `image: registry:9081/1mingo/reverseproxy-{cp,dp,fe}:latest` |
| 前端上下文 | `build: frontend/min-go-console` | 移除，统一用 registry 镜像 |
| migrate | `build: .` + `entrypoint: ["./efbundle"]` | `image: registry:9081/1mingo/reverseproxy-cp:latest` + `entrypoint` |
| 数据卷 | 命名卷 `cp-data` | 绑定挂载 `./data:/app/data`（Portainer 兼容） |

### 4. 保留 compose 本地构建能力

为了开发环境便捷性，在 `docker-compose.yml` 中保留 `build` 配置作为 fallback。当 `image` 标签拉取失败或开发者需要本地测试时，仍可 `docker compose build`。

## Risks / Trade-offs

- **[风险] Migrate 服务依赖 cp 镜像中携带 efbundle** → 确保 ControlPlane.Dockerfile 的构建产出包含 efbundle（已有）
- **[风险] 本地开发与 Portainer 共用同一份 compose** → 本地用 `build`，生产用 `image`，CI 推送 `latest` 后 Portainer 手动拉取部署
- **[风险] 前端 Dockerfile 引用路径为相对路径** → 已在 `frontend/min-go-console/` 内独立，不影响
