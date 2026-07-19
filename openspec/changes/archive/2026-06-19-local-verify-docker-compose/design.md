## Context

项目目前有 `docker-compose.yml` 用于生产部署，使用私有仓库镜像、Production 环境、bind mount 持久化数据库。本地开发验证缺少专用的编排配置。

ControlPlane.Api 的 Program.cs 中，`ASPNETCORE_ENVIRONMENT=Development` 不提供静态文件（前端需通过 Vite dev server 单独运行），而 `Production` 模式下提供静态文件但没有自动迁移。本设计采用 Production 模式+迁移服务模式，同时用环境变量覆盖日志级别。

## Goals / Non-Goals

**Goals:**
- 提供一键启动完整 MinGo 栈（ControlPlane + DataPlane + 前端）的本地编排
- 所有服务基于本地 Dockerfile 构建，不依赖外部 registry
- 数据库状态完全由 Docker 管理（named volume），`down -v` 即可重置
- 前端界面可访问 (localhost:5000)
- HTTPS (8443) 端口可用，证书由业务层加载
- 保留 migrate 服务模式，每次启动重新初始化数据库

**Non-Goals:**
- 不改动应用源代码（Program.cs、Dockerfile 等）
- 不改动现有生产 docker-compose.yml
- 不引入热重载或源码挂载（验证构建产物而非开发迭代）
- 不处理测试框架或 CI/CD 集成

## Decisions

1. **独立文件 vs override 文件**
   - 选择: 独立文件 `docker-compose.local.yml`
   - 理由: `docker-compose.override.yml` 会被 docker-compose 自动加载，容易在生产环境中意外生效
   - 替代方案: override 文件（自动加载导致风险）

2. **ASPNETCORE_ENVIRONMENT 选择**
   - 选择: `Production` + 环境变量覆盖日志级别
   - 理由: Development 模式下 `Program.cs` 不提供 wwwroot 静态文件（第106-109行），也无法 SPA fallback（第140-142行）。Production 模式正常提供前端 UI
   - 替代方案: Development 模式（无前端页面，不符合验证需求）

3. **数据库存储方式**
   - 选择: Docker named volume `mingocp-data`
   - 理由: 数据生命周期完全由 Docker 管理，`docker compose down -v` 即完全重置，不污染宿主文件系统
   - 替代方案: bind mount ./data（共享宿主机状态，重置麻烦）

4. **migrate 服务保留**
   - 选择: 保留，保持与生产一致的模式
   - 理由: ControlPlane 以 Production 模式启动时不会自动执行迁移，需要 migrate 服务先初始化数据库
   - 替代方案: 去掉 migrate，改为在 cp 启动脚本中调用 efbundle（复杂度增加）

5. **restart 策略**
   - 选择: `restart: "no"` 全部服务
   - 理由: 开发环境失败应暴露问题而非静默重启
   - 替代方案: `unless-stopped`（生产策略，开发环境不合适）

## Risks / Trade-offs

- **修改代码需要 rebuild 镜像**: 本地验证基于构建产物，改代码必须 `docker compose build` → 可接受，因为"验证"场景本身就是验证构建产物
- **migrate 和 cp 共享 volume 的竞态**: migrate 写入 SQLite，cp 读取同一文件。通过 `depends_on: condition: service_completed_successfully` 确保顺序
- **DataPlane 等待 ControlPlane 的 gRPC 就绪**: depends_on 只保证容器启动，不保证服务可用。DataPlane 代码中已有 `WaitForInitialConfigAsync(TimeSpan.FromSeconds(30))` 处理此场景
- **Windows 文件锁**: SQLite 文件在 Docker volume 中，不受宿主文件锁影响
