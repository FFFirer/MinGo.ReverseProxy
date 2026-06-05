## Why

当前 CI/CD 与实际项目架构脱节。项目已从单体拆分为 ControlPlane.Api + DataPlane + 独立前端三个独立组件，但 Gitea Actions 仍在构建旧单体镜像。Docker Compose 使用本地构建（`build: .`），无法在 Portainer 等远程环境中通过 registry 镜像部署。

## What Changes

- **重写 Gitea Actions workflow** — 从单体构建改为 matrix 并行构建 3 个镜像（cp/dp/fe），推送到私有 registry
- **替换 docker-compose.yml** — 从 `build: .` 改为 `image:` 从 registry 拉取，适配 Portainer Stack 部署
- **保留 migrate 服务** — efbundle 作为 one-shot 前置迁移不变
- **CD 流程简化** — CI 只负责构建推送，Portainer Stack 由运维手动触发部署

## Capabilities

### New Capabilities
- `ci-cd-pipeline`: Gitea Actions 自动化构建与推送 3 个容器镜像到私有 registry
- `portainer-stack-deployment`: 基于 registry 镜像的 docker-compose.yml，适配 Portainer Stack

### Modified Capabilities
（无 — 本次不涉及 spec 级行为变更）

## Impact

- `.gitea/workflows/build.yml` — 完整重写
- `docker-compose.yml` — 完整替换
- Dockerfiles（ControlPlane.Dockerfile、DataPlane.Dockerfile、frontend Dockerfile）— 无需修改
