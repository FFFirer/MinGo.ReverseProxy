# Dockerfile 构建框架说明

## 构建框架概述

本项目使用多阶段构建（Multi-stage Build）策略，分为三个主要阶段：

1. **前端构建阶段**：使用 Node.js 环境构建前端资源（TailwindCSS + Vite）
2. **后端构建阶段**：使用 .NET SDK 构建 .NET 应用
3. **运行时阶段**：使用 .NET ASP.NET Core 运行时镜像运行应用

# 模板

```Dockerfile
# Dockerfile 模板
# 说明：
# 1. 本模板使用多阶段构建策略
# 2. 请根据实际项目结构修改占位符
# 3. 占位符格式：[占位符名称]

# 第一阶段：构建前端
FROM swr.cn-north-4.myhuaweicloud.com/ddn-k8s/docker.io/library/node:20-alpine AS frontend-build

# 安装 pnpm
ARG NPM_REGISTRY=https://registry.npmjs.org/

ENV COREPACK_NPM_REGISTRY=${NPM_REGISTRY}
ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"

RUN npm install -g corepack@latest

RUN corepack enable && corepack prepare pnpm@latest --activate
RUN pnpm config set registry https://registry.npmmirror.com

# 设置工作目录
WORKDIR /app

# 复制 package.json 和 pnpm-lock.yaml 文件，用于缓存
# 注意：请修改为实际的前端项目路径
COPY [WebProjectDir]/package.json [WebProjectDir]/pnpm-lock.yaml .

# 还原 npm 包（使用缓存）
RUN pnpm install --frozen-lockfile

# 复制前端项目目录其余文件
# 注意：请修改为实际的前端项目路径
COPY [WebProjectDir] .

# 构建前端资源
RUN pnpm run build

# 第二阶段：构建 .NET 应用
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build

# 设置工作目录
WORKDIR /app

# 复制 .Net 的项目文件及解决方案文件，用于缓存
# 注意：请根据实际项目结构修改以下路径
COPY *.slnx .
COPY [WebProjectDir]/[WebProjectName].csproj [WebProjectDir]/
COPY [ApplicationProjectDir]/[ApplicationProjectName].csproj [ApplicationProjectDir]/
COPY [CoreProjectDir]/[CoreProjectName].csproj [CoreProjectDir]/
COPY [InfrastructureProjectDir]/[InfrastructureProjectName].csproj [InfrastructureProjectDir]/
# 可选：如果有SDK项目
# COPY [SdkProjectDir]/[SdkProjectName].csproj [SdkProjectDir]/

# 还原 nuget 包（使用缓存）
RUN dotnet restore

# 复制全部项目文件
COPY . .

# 复制前端构建产物
# 注意：请修改为实际的前端项目路径
COPY --from=frontend-build /app/wwwroot ./[WebProjectDir]/wwwroot

# 使用 Release 编译项目
RUN dotnet build --configuration Release

# 发布 Web 站点
# 注意：请修改为实际的前端项目路径和项目名称
RUN dotnet publish [WebProjectDir] --configuration Release --no-build --output /app/publish

# 第三阶段：发布
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# 设置工作目录
WORKDIR /app

# 复制发布文件
COPY --from=backend-build /app/publish .

# 创建数据目录（如果需要）
RUN mkdir -p /app/data

# 暴露端口
EXPOSE [Port]

# 设置环境变量
# 注意：请修改为实际的端口号
ENV ASPNETCORE_URLS=http://+:[Port]

# 运行应用
# 注意：请修改为实际的项目名称
ENTRYPOINT ["dotnet", "[WebProjectName].dll"]

# 占位符说明：
# [WebProjectDir] - 前端/Blazor项目目录路径，如 src/MyProject.Web
# [WebProjectName] - 前端/Blazor项目名称，如 MyProject.Web
# [ApplicationProjectDir] - 应用层项目目录路径，如 src/MyProject.Application
# [ApplicationProjectName] - 应用层项目名称，如 MyProject.Application
# [CoreProjectDir] - 核心层项目目录路径，如 src/MyProject.Core
# [CoreProjectName] - 核心层项目名称，如 MyProject.Core
# [InfrastructureProjectDir] - 基础设施层项目目录路径，如 src/MyProject.Infrastructure
# [InfrastructureProjectName] - 基础设施层项目名称，如 MyProject.Infrastructure
# [SdkProjectDir] - SDK项目目录路径（如果有），如 src/MyProject.SDK
# [SdkProjectName] - SDK项目名称（如果有），如 MyProject.SDK
# [Port] - 应用监听端口，如 8080
```