## 1. docker-compose.yml 修改

- [x] 1.1 新增 `migrate` 服务定义，使用 `ControlPlane.Dockerfile` 构建，设置 `entrypoint: ["./efbundle"]`，挂载 `cp-data` 卷，设置环境变量和 `restart: "no"`
- [x] 1.2 修改 `control-plane` 服务的 `depends_on`，添加 `migrate: condition: service_completed_successfully`
- [ ] 1.3 构建镜像并本地验证：`docker compose build migrate` 构建镜像；`docker compose run --rm migrate` 检查 efbundle 执行成功（需在本地 Docker 环境中执行）
- [ ] 1.4 启动全部服务：`docker compose up` 验证 control-plane 正常启动、data-plane 配置同步连接成功（需在本地 Docker 环境中执行）
