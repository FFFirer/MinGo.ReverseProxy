# ControlPlane.Dockerfile
# Stage 1: 前端构建
FROM swr.cn-north-4.myhuaweicloud.com/ddn-k8s/docker.io/library/node:22-alpine AS frontend-build

ARG NPM_REGISTRY=https://registry.npmjs.org/
ENV COREPACK_NPM_REGISTRY=${NPM_REGISTRY}
ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"

RUN npm install -g corepack@latest && \
    corepack enable && corepack prepare pnpm@latest --activate && \
    pnpm config set registry https://registry.npmmirror.com

WORKDIR /app

# 缓存前端依赖
COPY frontend/min-go-console/package.json frontend/min-go-console/pnpm-lock.yaml frontend/min-go-console/.npmrc /app/
RUN pnpm install --frozen-lockfile

# 构建前端（仅复制必要源文件）
COPY frontend/min-go-console/package.json frontend/min-go-console/pnpm-lock.yaml frontend/min-go-console/vite.config.ts frontend/min-go-console/tsconfig.json frontend/min-go-console/tsconfig.node.json frontend/min-go-console/index.html /app/
COPY frontend/min-go-console/src /app/src
COPY frontend/min-go-console/public /app/public
RUN pnpm run build

# Stage 2: 后端构建
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src

# 安装 ef 工具（版本与项目 EF Core 包一致）
RUN dotnet tool install -g dotnet-ef --version 10.0.0
ENV PATH="$PATH:/root/.dotnet/tools"

# 缓存 NuGet 包
COPY Directory.Packages.props .
COPY nuget.config .
COPY src/MinGo.Core/MinGo.Core.csproj src/MinGo.Core/
COPY src/MinGo.Application/MinGo.Application.csproj src/MinGo.Application/
COPY src/MinGo.Infrastructure/MinGo.Infrastructure.csproj src/MinGo.Infrastructure/
COPY src/MinGo.ControlPlane.Api/MinGo.ControlPlane.Api.csproj src/MinGo.ControlPlane.Api/
COPY src/MinGo.ControlPlane.Api/GrpcServices/Protos/dataplane.proto src/MinGo.ControlPlane.Api/GrpcServices/Protos/
RUN dotnet restore src/MinGo.ControlPlane.Api/MinGo.ControlPlane.Api.csproj

# 构建后端 + 复制前端产物
COPY . .
RUN dotnet publish src/MinGo.ControlPlane.Api -c Release -o /app/publish
COPY --from=frontend-build /app/dist /app/publish/wwwroot

# 生成 EF Core 迁移 Bundle
WORKDIR /src/src/MinGo.Infrastructure
RUN dotnet ef migrations bundle -c AppDbContext --configuration Release --no-build -o /app/publish/efbundle

# Stage 3: 运行阶段
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# 安装 curl 用于容器健康检查
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=backend-build /app/publish .
EXPOSE 5000 5001
VOLUME ["/app/data"]

HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=2 \
    CMD curl -fsS http://localhost:5000/healthz/live || exit 1

ENTRYPOINT ["dotnet", "MinGo.ControlPlane.Api.dll"]
