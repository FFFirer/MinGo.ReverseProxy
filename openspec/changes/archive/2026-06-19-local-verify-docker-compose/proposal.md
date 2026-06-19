## Why

现有 `docker-compose.yml` 为生产部署设计：镜像从私有仓库拉取、使用 Production 环境、数据库状态通过 bind mount 持久化在宿主机。本地开发验证时需要反复修改生产配置或者手动覆盖参数，流程割裂且容易误操作。

需要一个专用于本地开发+集成验证的 Docker Compose 配置，实现一键启动完整栈、数据生命周期完全由 Docker 管理、不从远端拉取任何依赖。

## What Changes

- 新增 `docker-compose.local.yml` 项目根目录
- 所有服务使用 `build:` 本地构建，不从 registry 拉取镜像
- 数据库使用 Docker named volume 存储，不 bind mount 宿主目录
- 使用 `ASPNETCORE_ENVIRONMENT=Production` 确保前端静态文件被正常服务
- 通过环境变量覆盖日志级别为 Debug（不修改 appsettings 文件）
- 去掉生产环境的重启策略和 registry 引用
- 保留 migrate 服务模式，每次 up 都执行数据库迁移

## Capabilities

### New Capabilities
- `local-verify-infra`: 本地开发验证的基础设施配置，包括容器编排、数据生命周期管理、构建策略

### Modified Capabilities

None — 纯新增基础设施配置，不修改现有能力。

## Impact

- 项目根目录新增 `docker-compose.local.yml` 文件
- 不影响现有 `docker-compose.yml`（生产配置）
- 不影响任何应用代码或现有 Dockerfile
- 开发者通过 `-f docker-compose.local.yml` 切换使用
