## 1. NuGet 依赖

- [x] 1.1 在 `Directory.Packages.props` 中添加 `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 版本 `10.0.0`
- [x] 1.2 在 `src/MinGo.ControlPlane.Api/MinGo.ControlPlane.Api.csproj` 中添加 PackageReference（通过 CPM，无版本号）

## 2. ControlPlane 健康检查端点

- [x] 2.1 在 `src/MinGo.ControlPlane.Api/Program.cs` 中注册 `AddHealthChecks().AddDbContextCheck<AppDbContext>(tags: ["ready"])`
- [x] 2.2 在 `src/MinGo.ControlPlane.Api/Program.cs` 中映射 `GET /healthz/live`（`Predicate = _ => false`）
- [x] 2.3 在 `src/MinGo.ControlPlane.Api/Program.cs` 中映射 `GET /healthz/ready`（`Predicate = check => check.Tags.Contains("ready")`）
- [x] 2.4 在 `src/MinGo.ControlPlane.Api/Program.cs` 中配置 `ResultStatusCodes`（Healthy → 200，Degraded/Unhealthy → 503）

## 3. ControlPlane Dockerfile

- [x] 3.1 在 `ControlPlane.Dockerfile` 的 runtime stage 中安装 `curl`（`apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*`）
- [x] 3.2 在 `ControlPlane.Dockerfile` 的 runtime stage 中添加 `HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=2 CMD curl -fsS http://localhost:5000/healthz/live || exit 1`

## 4. DataPlane 健康检查端点

- [x] 4.1 在 `src/MinGo.DataPlane/Program.cs` 中注册 `AddHealthChecks()`
- [x] 4.2 在 `src/MinGo.DataPlane/Program.cs` 中映射 `GET /healthz/live`（`Predicate = _ => false`）

## 5. DataPlane Dockerfile

- [x] 5.1 在 `DataPlane.Dockerfile` 的 runtime stage 中安装 `curl`
- [x] 5.2 在 `DataPlane.Dockerfile` 的 runtime stage 中添加 `HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 CMD curl -fsS http://localhost:8080/healthz/live || exit 1`

## 6. DataPlane 启动超时

- [x] 6.1 在 `src/MinGo.DataPlane/ConfigSync/ConfigSyncService.cs` 中将 `WaitForInitialConfigAsync` 超时从 30s 改为 60s

## 7. docker-compose.yml

- [x] 7.1 为 `control-plane` 服务添加 `healthcheck:` 配置（探测 `/healthz/ready`，interval=15s，timeout=5s，retries=2，start_period=15s）
- [x] 7.2 将 `data-plane` 服务的 `depends_on` 从 `- control-plane` 升级为 `control-plane: condition: service_healthy`

## 8. docker-compose.local.yml

- [x] 8.1 为 `control-plane` 服务添加 `healthcheck:` 配置（与 production 相同）
- [x] 8.2 将 `data-plane` 服务的 `depends_on` 从 `- control-plane` 升级为 `control-plane: condition: service_healthy`
- [x] 8.3 确认 `data-plane` 的 `restart: "no"` 保持不变
