## Context

MinGo ReverseProxy 当前部署架构包含 3 个独立容器：`frontend`（nginx + SolidJS 静态文件）、`control-plane`（ASP.NET Core API + gRPC）、`data-plane`（YARP 代理引擎）。前端通过 nginx 反向代理 `/api` 请求到 control-plane。

目标是消除独立的 frontend 容器，在部署时将前端构建产物嵌入 control-plane 镜像的 `wwwroot/`，由 ASP.NET Core 统一输出 API + 静态文件。

### 当前状态

| 组件 | 技术栈 | 端口 | 职责 |
|------|--------|------|------|
| `frontend` | nginx + SolidJS | :8081 | 静态文件服务 + SPA 路由 + /api 反向代理 |
| `control-plane` | ASP.NET Core 10 | :5000 (HTTP), :5001 (gRPC) | REST API + gRPC + 业务逻辑 |
| `data-plane` | ASP.NET Core 10 / YARP | :8080 | 反向代理引擎 |

### 关键约束

- 前端 API 请求使用相对路径 `const API_BASE = '/api'`（已验证）
- 开发环境保持 Vite Dev Server（:5173）+ ASP.NET（:5000）分离
- `src/MinGo.ReverseProxy` 已废弃归档，不涉及
- 构建需要 Docker multi-stage 能力

## Goals / Non-Goals

**Goals:**
- 生产环境中 ControlPlane 单个进程/端口同时提供 REST API + 静态前端
- 消除独立 frontend nginx 容器和 docker-compose 中的 frontend service
- 开发环境零影响：Vite Dev Server + HMR + CORS proxy 保持原样
- 保留 `frontend/min-go-console/Dockerfile` 供独立构建/调试场景

**Non-Goals:**
- 不改变 data-plane 的部署结构
- 不涉及前端代码逻辑修改（API 路径已使用相对路径）
- 不改变 gRPC 端口和协议配置
- 不涉及 CI/CD 流程的变更（Dockerfile 变更后自动生效）

## Decisions

### D1: Docker 构建策略 — 3-stage 合并构建

**选择**：在 `ControlPlane.Dockerfile` 中执行前端构建，而非 CI 中分步构建。

**理由**：
- 单 Dockerfile 自包含，CI/CD 配置最简
- 前端构建产物不需跨 CI stage 传递
- 版本一致性保证（同一镜像包含匹配的前端+后端）
- layer caching 可优化前端依赖安装（package.json 单独 COPY）

**备选方案**：CI 分步构建前端 → 上传 artifact → 后端构建引用 → 被拒绝（CI 配置复杂、版本一致性难以强制）

### D2: 前端构建的 layer caching 策略

**选择**：利用 Docker layer cache，按变更频率排序 COPY。

```dockerfile
# 高命中率缓存层
COPY frontend/min-go-console/package.json frontend/min-go-console/pnpm-lock.yaml frontend/min-go-console/.npmrc /app/
RUN corepack enable && pnpm install --frozen-lockfile

# 低命中率层（源码变更频繁）
COPY frontend/min-go-console/ /app/
RUN pnpm run build
```

**理由**：前端依赖（`package.json` + `lock`）变更远少于源码变更，将此层前置可大幅提升重复构建速度。

### D3: ASP.NET 中间件注册顺序

**选择**：静态文件中间件在 `UseRouting()` 之前，SPA fallback 在所有路由映射之后。

```csharp
// 生产环境：UseDefaultFiles + UseStaticFiles 必须在 UseRouting 之前
// (静态文件不经过路由匹配，直接返回)
if (!app.Environment.IsDevelopment())
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<...>();

// SPA fallback 必须在所有路由映射之后
// 否则会吞掉 API/gRPC 路由未匹配时的 404 响应
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}
```

**理由**：
- `UseStaticFiles()` 为 ASP.NET Core 约定：静态文件中间件在管道早期、路由之前执行
- `MapFallbackToFile()` 是最低优先级路由：仅在 API / gRPC 路由全部未匹配时生效
- 此顺序避免了 SPA fallback 吞掉 API 404 的常见陷阱

### D4: CORS 策略 — 开发环境专属

**选择**：CORS 中间件仅在 `Development` 环境注册。

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseCors("Frontend");
}
```

**理由**：
- 生产环境同源（同一 :5000 端口），CORS 无意义
- 减少生产环境的攻击面和不必要的响应头
- `Frontend:Url` 配置项从 base `appsettings.json` 移除，仅在 `appsettings.Development.json` 中保留

### D5: wwwroot 输出路径

**选择**：前端 `dist/` 复制到 ASP.NET 发布目录下的 `wwwroot/`。

```dockerfile
COPY --from=frontend-build /app/dist /app/publish/wwwroot
```

**理由**：
- `UseStaticFiles()` 默认从 `ContentRoot/wwwroot` 提供文件
- 发布后 `ContentRoot` 即为 DLL 所在目录（`/app/publish`），`wwwroot/` 在此目录下自动被识别
- 无需在 `Program.cs` 中显式指定 `UseStaticFiles` 的路径参数

## Risks / Trade-offs

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| **前端构建失败阻塞整个镜像** | `pnpm run build` 失败导致 ControlPlane 镜像构建失败 | 这是预期行为 — 集成失败应尽早暴露。CI 中可先单独执行前端构建验证 |
| **Docker layer cache 失效** | 修改 `package.json` 导致前端依赖层重装 | 可接受 — 依赖变更时应完整重装 |
| **前端构建时间增加** | 增加 ~1-2 分钟构建时间 | layer cache 命中后仅增量构建，时间可接受 |
| **前端版本与后端耦合** | 前端更新需重新构建完整 ControlPlane 镜像 | 符合 monorepo 模式的预期行为；如需独立发布前端，仍可使用独立的 `frontend/min-go-console/Dockerfile` |
| **MapFallbackToFile 路由优先级** | 如果顺序错误，API 404 被 SPA fallback 吞掉，返回 index.html 而非 404 JSON | 设计 D3 明确规定了注册顺序，tasks 中需加入验证步骤 |

## Migration Plan

### 步骤

1. 修改 `ControlPlane.Dockerfile` — 增加 frontend-build stage
2. 修改 `Program.cs` — 调整中间件管道
3. 修改 `docker-compose.yml` — 移除 frontend 服务
4. 清理 `appsettings.json` — 移除 `Frontend:Url`
5. 构建并本地验证

### 回滚策略

- 保留独立的 `frontend/min-go-console/Dockerfile` 和 `nginx.conf`，docker-compose 中注释而非删除 frontend 服务定义
- 如有问题，取消注释 frontend 服务，恢复 `docker-compose.yml` 即可秒级回滚

## Open Questions

无。所有设计决策已在上述章节明确。
