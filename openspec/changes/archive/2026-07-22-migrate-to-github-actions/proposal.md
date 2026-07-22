## Why

将现有的 Gitea Actions CI 工作流迁移到 GitHub Actions，移除国内镜像加速和 npm 国内源，确保在 GitHub Actions 原生环境中正常工作。

## What Changes

- **新建** `.github/workflows/build.yml`：基于 `.gitea/workflows/build.yml` 转换，移除 BuildKit 镜像加速配置（`buildkitd-config-inline`）
- **修改** `Dockerfile`：移除 `pnpm config set registry https://registry.npmmirror.com` 行（方案A：保留 `NPM_REGISTRY` ARG 和 `COREPACK_NPM_REGISTRY` ENV，其默认值已是官方源 `registry.npmjs.org`）
- **保留** `.gitea/workflows/build.yml` 不变（或待确认是否删除）

## Capabilities

### New Capabilities

无新增能力。纯 CI/CD 基础设施迁移。

### Modified Capabilities

无 spec 级别行为变更。

## Impact

- 新增 `.github/workflows/build.yml`（GitHub Actions 工作流）
- 修改 `Dockerfile`（移除国内 npm 源配置）
- GitHub 仓库需手动配置 Actions secrets/variables：`DOCKER_REGISTRY`、`DOCKER_USERNAME`、`DOCKER_PASSWORD`
