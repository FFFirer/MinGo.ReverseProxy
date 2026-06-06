## Why

当前部署结构中，前端 `min-go-console`（SolidJS + Vite）通过独立 nginx 容器提供服务，与 ASP.NET Core ControlPlane API 分离部署。这导致：

- 部署需管理 3 个容器（frontend / control-plane / data-plane）
- 生产环境存在不必要的 CORS 配置和跨域请求
- 前端 nginx 容器仅做静态文件 + API 反向代理，可被 ControlPlane 直接替代
- 部署拓扑复杂化

目标：开发时保持 Vite Dev Server 的 HMR/前后端分离体验，**部署时**将前端构建产物直接嵌入 ControlPlane 镜像的 `wwwroot/`，由 ASP.NET Core 统一输出。

## What Changes

### ControlPlane.Dockerfile — 3-stage 构建

保留独立的 `frontend/min-go-console/Dockerfile` 供独立运行/调试，`ControlPlane.Dockerfile` 改为 3-stage：

```
Stage 1: frontend-build (node:22-alpine)
  ├─ COPY package.json + pnpm-lock.yaml
  ├─ RUN corepack enable && pnpm install --frozen-lockfile  (缓存层)
  ├─ COPY frontend/min-go-console/ .
  └─ RUN pnpm run build → /app/dist/

Stage 2: backend-build (dotnet/sdk:10.0)
  ├─ dotnet restore (缓存层)
  ├─ dotnet publish -c Release -o /app/publish
  └─ COPY --from=frontend-build /app/dist/ → /app/publish/wwwroot/

Stage 3: runtime (dotnet/aspnet:10.0)
  ├─ COPY --from=backend-build /app/publish .
  └─ ENTRYPOINT ["dotnet", "MinGo.ControlPlane.Api.dll"]
```

### Program.cs — 中间件管道调整

CORS 仅在开发环境启用；生产环境增加静态文件服务 + SPA 回退，且顺序必须精确：

```csharp
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseCors("Frontend");   // 开发环境：前端在独立端口
}
else
{
    app.UseDefaultFiles();     // 生产环境：默认文档 index.html
    app.UseStaticFiles();      // 生产环境：wwwroot 静态文件
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<ConfigReplicationService>();
app.MapGrpcService<HeartbeatCollectService>();
app.MapGrpcService<EventSubscriptionService>();

if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");  // SPA 回退，必须在所有路由之后
}

app.Run();
```

关键约束：`UseDefaultFiles()` + `UseStaticFiles()` 在 `UseRouting()` 之前（静态文件不经过路由）；`MapFallbackToFile()` 在所有 `MapControllers/MapGrpcService` 之后（防止吞掉 API/gRPC 404）。

### docker-compose.yml

- 移除 `frontend` 服务
- `control-plane` 现有 `:5000` 端口同时承载 API + 前端静态文件，无需新增端口

### appsettings.json

- 从 base `appsettings.json` 中移除 `Frontend:Url`（仅 CORS 使用，已变为开发环境专属）
- `appsettings.Development.json` 无需变更（如有需要可保留或移除）

### 不变的文件

| 文件 | 处理 |
|------|------|
| `frontend/min-go-console/vite.config.ts` | API 使用 `/api/xxx` 相对路径 ✅ 已验证无需变更 |
| `frontend/min-go-console/nginx.conf` | 部署场景不再使用，保留供独立运行/调试 |
| `frontend/min-go-console/Dockerfile` | 保留，供独立构建场景使用 |
| `src/MinGo.ReverseProxy` | 已废弃归档，不做任何变更 |

## Capabilities

### New Capabilities

- `frontend-deploy-embed`：控制面统一部署 — 前端构建产物嵌入 ControlPlane 镜像，生产环境同源输出 API + 静态文件，开发环境保持前后端分离

### Modified Capabilities

无。本次不涉及功能需求变更，仅改变部署结构。

## Impact

| 层面 | 影响 |
|------|------|
| **构建流水线** | `ControlPlane.Dockerfile` 增加 Node 构建 Stage（node:22-alpine），构建时间增加 ~1-2 分钟；Docker layer 缓存可将前端依赖安装缓存命中 |
| **Docker 镜像** | ControlPlane 镜像体积增加前端静态产物（~1-2MB），但整体部署容器数从 3 降为 2 |
| **运行时** | ASP.NET 管道增加静态文件中间件（UseDefaultFiles + UseStaticFiles），性能影响可忽略 |
| **开发体验** | 不变 — 开发者继续使用 `pnpm dev`（:5173）+ `dotnet run`（:5000）分离开发，Vite proxy + CORS 维持原状 |
| **部署拓扑** | docker-compose 简化，不再需要独立 frontend 服务；`control-plane:5000` 统一输出 |
| **E2E 测试** | Playwright 基于 `localhost:5173` Vite Dev Server，不受影响 |
