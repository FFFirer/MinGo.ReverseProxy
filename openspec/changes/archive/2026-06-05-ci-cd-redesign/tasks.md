## 1. Gitea Actions Workflow

- [x] 1.1 重写 `.gitea/workflows/build.yml` — 从单体构建改为 matrix 策略，并行构建 `cp`/`dp`/`fe` 三个组件
- [x] 1.2 Matrix 中 `cp` — 使用 `ControlPlane.Dockerfile`，镜像名 `reverseproxy-cp`
- [x] 1.3 Matrix 中 `dp` — 使用 `DataPlane.Dockerfile`，镜像名 `reverseproxy-dp`
- [x] 1.4 Matrix 中 `fe` — 使用 `Dockerfile`（context: `frontend/min-go-console`），镜像名 `reverseproxy-fe`
- [x] 1.5 注册表登录使用 `docker/login-action`，从 `vars`/`secrets` 读取认证信息
- [x] 1.6 版本标签：release 用 tag name，workflow_dispatch 用 `dev-{sha}-{timestamp}`，均附带 `latest`
- [x] 1.7 镜像推送到 `registry.private.fffirer.top:9081/1mingo/` 命名空间

## 2. docker-compose.yml

- [x] 2.1 替换 `docker-compose.yml` — 服务使用 registry 镜像而非本地构建
- [x] 2.2 `migrate` 服务使用 cp 镜像 + `efbundle` entrypoint，绑定数据卷
- [x] 2.3 `control-plane` 服务使用 `reverseproxy-cp` 镜像，映射端口 5000/5001，配置环境变量
- [x] 2.4 `data-plane` 服务使用 `reverseproxy-dp` 镜像，映射端口 8080，配置 gRPC 连接地址
- [x] 2.5 `frontend` 服务使用 `reverseproxy-fe` 镜像，映射端口 8081:80
- [x] 2.6 保留 `build` 上下文作为本地开发 fallback
- [x] 2.7 使用 `./data:/app/data` 绑定挂载持久化 SQLite 数据
