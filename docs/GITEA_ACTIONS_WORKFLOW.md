# Gitea Actions Workflow 构建脚本说明文档

## 概述

本文档详细介绍 [项目名称] 项目的 Gitea Actions Workflow 构建脚本，该脚本用于自动化构建和发布 Docker 镜像。

## 工作流文件位置

```
.gitea/workflows/build.yml
```

## 工作流配置详解

### 基本信息

```yaml
name: Build and Release

on:
  release:
    types: [ published, prereleased, released ]
  workflow_dispatch:
```

- **名称**：Build and Release
- **触发条件**：
  - 当发布新版本时（published, prereleased, released）
  - 手动触发（workflow_dispatch）

### 环境变量

```yaml
env:
  CONTAINER_REGISTRY: ${{ vars.DOCKER_REGISTRY }}
  CONTAINER_REGISTRY_USERNAME: ${{ vars.DOCKER_USERNAME }}
  CONTAINER_REGISTRY_PASSWORD: ${{ secrets.DOCKER_PASSWORD }}
  IMAGE_NAME: [镜像名称]
```

- **CONTAINER_REGISTRY**：Docker 镜像仓库地址，通过 Gitea 仓库变量配置
- **CONTAINER_REGISTRY_USERNAME**：Docker 镜像仓库用户名，通过 Gitea 仓库变量配置
- **CONTAINER_REGISTRY_PASSWORD**：Docker 镜像仓库密码，通过 Gitea 仓库密钥配置
- **IMAGE_NAME**：Docker 镜像名称

### 构建任务

```yaml
jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Login to Docker Registry
        uses: docker/login-action@v3
        with:
          registry: ${{ env.CONTAINER_REGISTRY }}
          username: ${{ env.CONTAINER_REGISTRY_USERNAME }}
          password: ${{ env.CONTAINER_REGISTRY_PASSWORD }}

      - name: Extract version from tag
        id: extract_version
        run: echo "VERSION=${GITHUB_REF#refs/*/}" >> $GITHUB_ENV

      - name: Build and push Docker image
        uses: docker/build-push-action@v5
        with:
          context: .
          push: ${{ github.event_name == 'release' }}
          tags: |
            ${{ env.CONTAINER_REGISTRY }}/${{ env.IMAGE_NAME }}:${{ env.VERSION }}
            ${{ env.CONTAINER_REGISTRY }}/${{ env.IMAGE_NAME }}:latest
```

- **运行环境**：Ubuntu 最新版本
- **执行步骤**：
  1. 检出代码
  2. 设置 Docker Buildx
  3. 登录到 Docker 镜像仓库
  4. 提取版本号
    - 当触发事件为 release 时，从 Git 标签中提取版本号
    - 当手动触发时，使用 `dev-${COMMIT_SHA}-${TIMESTAMP}` 格式，其中：
      - COMMIT_SHA 是 Git 提交的短哈希值
      - TIMESTAMP 是构建时的时间戳，格式为 YYYYMMDDHHMMSS
  5. 构建并推送 Docker 镜像
    - 构建上下文：项目根目录
    - 推送条件：总是推送
    - 标签：
      - `${CONTAINER_REGISTRY}/${IMAGE_NAME}:${VERSION}` - 带版本号的标签
      - `${CONTAINER_REGISTRY}/${IMAGE_NAME}:latest` - 最新版本标签

## 配置要求

### Gitea 仓库配置

在使用此工作流之前，需要在 Gitea 仓库中配置以下变量和密钥：

| 类型 | 名称 | 说明 |
|------|------|------|
| 变量 | DOCKER_REGISTRY | Docker 镜像仓库地址（例如：registry.example.com） |
| 变量 | DOCKER_USERNAME | Docker 镜像仓库用户名 |
| 密钥 | DOCKER_PASSWORD | Docker 镜像仓库密码 |

### Dockerfile 要求

项目根目录需要存在有效的 `Dockerfile` 文件，用于构建 Docker 镜像。

## 使用方法

### 自动触发

当创建新的发布版本时，工作流会自动触发：

1. 在 Gitea 仓库中创建新的发布版本
2. 选择发布类型（正式发布、预发布等）
3. 工作流会自动开始执行，构建并推送 Docker 镜像

### 手动触发

可以通过 Gitea 界面手动触发工作流：

1. 进入仓库的 Actions 页面
2. 选择 "Build and Release" 工作流
3. 点击 "Run workflow" 按钮
4. 确认后工作流开始执行

## 构建产物

成功执行后，工作流会生成并推送以下 Docker 镜像：

- `${CONTAINER_REGISTRY}/[镜像名称]:${VERSION}` - 带版本号的镜像
  - 当触发事件为 release 时，版本号为 Git 标签（例如：v1.0.0）
  - 当手动触发时，版本号为 `dev-${COMMIT_SHA}-${TIMESTAMP}` 格式（例如：dev-abc123-20240101120000）
- `${CONTAINER_REGISTRY}/[镜像名称]:latest` - 最新版本镜像

## 故障排除

### 常见问题

1. **Docker 登录失败**
   - 检查 `DOCKER_REGISTRY`、`DOCKER_USERNAME` 和 `DOCKER_PASSWORD` 配置是否正确
   - 确保 Docker 镜像仓库凭证有效

2. **构建失败**
   - 检查 `Dockerfile` 是否存在且配置正确
   - 确保项目能够正常构建

3. **推送失败**
   - 检查 Docker 镜像仓库权限
   - 确保网络连接正常

### 日志查看

在 Gitea 仓库的 Actions 页面可以查看工作流执行日志，了解执行过程中的详细信息和可能的错误。

## 最佳实践

1. **版本管理**
   - 使用语义化版本号（例如：v1.0.0）
   - 确保 Git 标签与发布版本一致

2. **安全性**
   - 不要在代码中硬编码 Docker 镜像仓库凭证
   - 使用 Gitea 仓库密钥存储敏感信息

3. **构建优化**
   - 优化 Dockerfile 以减少镜像大小
   - 考虑使用多阶段构建

## 示例配置

### 示例环境变量配置

```bash
# Gitea 仓库变量
DOCKER_REGISTRY=registry.example.com
DOCKER_USERNAME=myusername

# Gitea 仓库密钥
DOCKER_PASSWORD=mypassword
```

### 示例执行结果

**发布触发时**：
当创建版本 `v1.0.0` 时，工作流会构建并推送以下镜像：

- `registry.example.com/[镜像名称]:v1.0.0`
- `registry.example.com/[镜像名称]:latest`

**手动触发时**：
当手动触发工作流时，工作流会构建并推送以下镜像（假设 commit sha 为 abc123，时间戳为 20240101120000）：

- `registry.example.com/[镜像名称]:dev-abc123-20240101120000`
- `registry.example.com/[镜像名称]:latest`

## 结论

此 Gitea Actions Workflow 构建脚本提供了一种自动化的方式来构建和发布 [项目名称] 项目的 Docker 镜像。通过正确配置和使用，可以确保每次发布时都能获得一致、可靠的 Docker 镜像。

该脚本支持两种版本生成方式：
- 发布触发时：使用 Git 标签作为版本号
- 手动触发时：使用 `dev-${COMMIT_SHA}-${TIMESTAMP}` 格式，包含：
  - Git 提交的短哈希值
  - 构建时的时间戳（格式：YYYYMMDDHHMMSS）

这种设计使得镜像版本更加清晰，同时在开发过程中也能通过 commit sha 和时间戳快速定位构建来源，避免同一 commit 多次构建时的版本冲突。