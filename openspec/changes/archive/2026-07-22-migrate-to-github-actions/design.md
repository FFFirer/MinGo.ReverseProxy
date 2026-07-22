## Context

当前项目使用 Gitea Actions 作为 CI/CD 系统，工作流位于 `.gitea/workflows/build.yml`。该工作流在 release 事件或手动触发时，构建两个 Docker 组件（ControlPlane、DataPlane）并推送到私有 registry。由于平台从 Gitea 迁移到 GitHub，需要将 CI/CD 迁移至 GitHub Actions。

现有工作流中使用了国内镜像加速（BuildKit mirror）和 npm 国内源（npmmirror），这些在 GitHub Actions 的海外 runner 上不需要。

## Goals / Non-Goals

**Goals:**
- 创建 `.github/workflows/build.yml`，功能与 `.gitea/workflows/build.yml` 等价
- 移除 BuildKit Docker 镜像加速配置
- 移除 Dockerfile 中 `pnpm config set registry` 国内源配置
- 保留 `NPM_REGISTRY` ARG 和 `COREPACK_NPM_REGISTRY` ENV（方案A）

**Non-Goals:**
- 不修改构建逻辑、矩阵策略或镜像命名规则
- 不修改 Dockerfile 的其他部分
- 不处理 GitHub Secrets/Variables 的配置（需用户在仓库设置中手动配置）
- 不删除 `.gitea/workflows/build.yml`（保留原文件）

## Decisions

| 决策 | 选择 | 理由 |
|------|------|------|
| BuildKit 镜像加速 | 移除 `buildkitd-config-inline` | GitHub Actions runner 位于海外，直连 Docker Hub 速度正常 |
| npm 国内源 | 移除 `pnpm config set registry` | 同上，海外 runner 直连 npmjs.org 更快 |
| `NPM_REGISTRY` ARG | 保留 | 默认值已是官方 `registry.npmjs.org`，保留不产生副作用，且为未来自定义 registry 留入口 |
| `.gitea/workflows/build.yml` | 保留 | 保留历史配置，无冲突风险 |
| 工作流触发方式 | 完全保留 | `release` + `workflow_dispatch` 在 GitHub Actions 中同样支持 |

## Risks / Trade-offs

- [低] GitHub Actions runner 网络策略可能影响私有 registry 访问 → 需用户确认 registry 地址从 GitHub 网络可达
- [低] Gitea `vars.XXX` 和 GitHub `vars.XXX` 语法相同，但配置入口不同 → 需用户在 GitHub 仓库 Settings → Secrets and variables → Actions 中手动配置 `DOCKER_REGISTRY`、`DOCKER_USERNAME`、`DOCKER_PASSWORD`
