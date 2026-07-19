## ADDED Requirements

### Requirement: 开发环境前后端分离

开发时前端通过 Vite Dev Server 独立运行，API 请求通过 Vite proxy 转发到 ControlPlane。

#### Scenario: Vite Dev Server 启动

- **WHEN** 开发者在 `frontend/min-go-console/` 目录执行 `pnpm dev`
- **THEN** Vite Dev Server 在 `:5173` 端口启动，支持 HMR

#### Scenario: API 请求代理

- **WHEN** 前端发起 `/api/*` 请求
- **THEN** Vite Dev Server 将请求代理到 `http://localhost:5000`

#### Scenario: ControlPlane 开发启动

- **WHEN** 开发者在 `src/MinGo.ControlPlane.Api/` 执行 `dotnet run`
- **THEN** ControlPlane 在 `http://localhost:5000` 启动，注册 CORS 中间件允许 `http://localhost:5173` 跨域

### Requirement: 生产环境同源部署

构建时前端产物嵌入 ControlPlane 镜像，运行时同一端口输出 API + 前端静态文件。

#### Scenario: Docker 镜像构建

- **WHEN** 执行 `docker build -f ControlPlane.Dockerfile`
- **THEN** Stage 1 使用 `node:22-alpine` 构建前端，输出到 `/app/dist/`
- **AND** Stage 2 使用 `dotnet/sdk:10.0` 构建后端，前端产物复制到 `/app/publish/wwwroot/`
- **AND** Stage 3 使用 `dotnet/aspnet:10.0` 运行最终镜像

#### Scenario: 生产环境静态文件服务

- **WHEN** ControlPlane 以 `Production` 环境启动
- **THEN** `UseDefaultFiles()` + `UseStaticFiles()` 中间件注册，从 `wwwroot/` 提供前端静态文件
- **AND** 访问 `/` 返回 `index.html`

#### Scenario: SPA 路由回退

- **WHEN** 用户访问 `/login`、`/clusters` 等前端路由
- **AND** 该路径不匹配任何 API 或 gRPC 路由
- **THEN** `MapFallbackToFile("index.html")` 返回前端 `index.html`

#### Scenario: API 路由优先级高于 SPA fallback

- **WHEN** 用户访问 `/api/clusters`
- **THEN** `MapControllers()` 匹配到对应的 API Controller 处理请求
- **AND** SPA fallback 不拦截该路径

#### Scenario: API 404 不被 SPA fallback 吞没

- **WHEN** 用户访问 `/api/nonexistent`
- **AND** 不存在匹配的 API 路由
- **THEN** 返回 HTTP 404 响应（非 `index.html`）

### Requirement: 前端 API 路径不变

前端代码中使用相对路径 `/api/xxx` 请求后端 API，无需因部署结构变更而修改。

#### Scenario: API 基础路径

- **WHEN** 前端发起 `fetch('/api/auth/login')`
- **THEN** 请求到达 `ControlPlane` 的 `/api/auth/login` 端点
- **AND** 开发环境通过 Vite proxy 转发，生产环境同源直接请求

### Requirement: CORS 仅开发环境启用

生产环境同源无需 CORS；CORS 中间件仅在 `Development` 环境下注册。

#### Scenario: 开发环境 CORS

- **WHEN** `ASPNETCORE_ENVIRONMENT=Development`
- **THEN** `app.UseCors("Frontend")` 注册，允许 `http://localhost:5173` 跨域

#### Scenario: 生产环境无 CORS

- **WHEN** `ASPNETCORE_ENVIRONMENT=Production`
- **THEN** CORS 中间件不注册
- **AND** 响应头中不包含 `Access-Control-*` 头

### Requirement: docker-compose 简化

独立的 `frontend` 服务从 `docker-compose.yml` 中移除，`control-plane:5000` 统一输出。

#### Scenario: 部署容器缩减

- **WHEN** 执行 `docker compose up`
- **THEN** 启动的容器为 `control-plane` 和 `data-plane` 两个（以及 `migrate` 一次性任务）
- **AND** 不再启动 `frontend` nginx 容器

#### Scenario: 前端访问端口

- **WHEN** 用户通过浏览器访问 `http://host:5000`
- **THEN** 获得前端 SPA 页面（由 ASP.NET Core 静态文件中间件提供）
