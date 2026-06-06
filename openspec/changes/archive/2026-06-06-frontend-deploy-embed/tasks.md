## 1. Docker 构建 — ControlPlane.Dockerfile

- [x] 1.1 在 `ControlPlane.Dockerfile` 中增加 frontend-build Stage（node:22-alpine），先 COPY package.json + pnpm-lock.yaml 安装依赖（layer cache），再 COPY 源码执行 `pnpm run build`
- [x] 1.2 在 backend-build Stage 中增加 COPY 指令：`COPY --from=frontend-build /app/dist /app/publish/wwwroot`
- [x] 1.3 验证 dotnet build 成功（0 error）；Docker 构建已验证 `podman compose config` 输出正确，完整构建需可用 Docker 环境

## 2. ASP.NET 中间件 — Program.cs

- [x] 2.1 在 `app.Build()` 后增加条件判断：非 Development 环境时注册 `app.UseDefaultFiles()` + `app.UseStaticFiles()`
- [x] 2.2 在所有 `MapControllers()` 和 `MapGrpcService()` 之后增加 SPA fallback：`if (!env.IsDevelopment()) app.MapFallbackToFile("index.html")`
- [x] 2.3 将 `app.UseCors("Frontend")` 包裹在 `if (app.Environment.IsDevelopment())` 条件中
- [x] 2.4 运行 `lsp_diagnostics` 确认无编译错误

## 3. 配置清理 — appsettings.json

- [x] 3.1 从 `src/MinGo.ControlPlane.Api/appsettings.json` 中移除 `Frontend` 配置节
- [x] 3.2 （可选）CORS fallback 值 `http://localhost:5173` 已在 Program.cs 中硬编码，无需加入 Development.json

## 4. 部署拓扑 — docker-compose.yml

- [x] 4.1 从 `docker-compose.yml` 中移除 `frontend` service 定义（注释方式保留，便于回滚）
- [x] 4.2 确认 `control-plane` service 的端口映射 `5000:5000` 足以承载 API + 前端静态文件
- [x] 4.3 验证 `podman compose config` 输出正确，仅包含 control-plane / data-plane / migrate，无 frontend 服务引用

## 5. 集成验证（需 Docker 环境 + 运行时）

- [ ] 5.1 构建完整镜像：`docker compose build control-plane`，确认构建成功
- [ ] 5.2 本地启动：`docker compose up control-plane`，访问 `http://localhost:5000` 确认前端页面正常加载
- [ ] 5.3 验证 API 正常工作：`curl http://localhost:5000/api/...` 返回正常 JSON
- [ ] 5.4 验证 SPA 路由：直接访问 `http://localhost:5000/login` 返回前端页面而非 404
- [ ] 5.5 验证 API 404 返回：`curl http://localhost:5000/api/nonexistent` 返回 JSON 404 而非 index.html
- [ ] 5.6 开发环境回归：分别启动 `pnpm dev` 和 `dotnet run`，确认 HMR + CORS + API proxy 正常工作
