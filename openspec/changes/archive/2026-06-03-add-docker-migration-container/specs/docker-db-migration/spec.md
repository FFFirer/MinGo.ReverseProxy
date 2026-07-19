## ADDED Requirements

### Requirement: Docker Compose 启动前自动运行数据库迁移
系统 SHALL 在 docker-compose 中定义一个一次性服务，在 control-plane 容器启动前执行 EF Core 数据库迁移。

#### Scenario: 迁移容器成功运行
- **WHEN** 执行 `docker-compose up`
- **THEN** `migrate` 容器先启动，运行 efbundle 完成迁移
- **THEN** `migrate` 容器以退出码 0 退出
- **THEN** `control-plane` 容器启动且数据库 Routes/Clusters/Certificates 表已存在

#### Scenario: 迁移容器运行失败
- **WHEN** `migrate` 容器中 efbundle 执行失败（如数据库文件损坏）
- **THEN** `migrate` 容器以非 0 退出码退出
- **THEN** `control-plane` 容器不启动（`depends_on: condition: service_completed_successfully`）

### Requirement: 复用控制面镜像构建产物
迁移容器 SHALL 使用与控制面相同的 `ControlPlane.Dockerfile` 构建镜像，通过覆盖 entrypoint 运行 `/app/efbundle`。

#### Scenario: 共享镜像
- **WHEN** `docker-compose build` 执行
- **THEN** `migrate` 与 `control-plane` 共用同一镜像 (ControlPlane.Dockerfile)
- **THEN** `migrate` 的 entrypoint 为 `["./efbundle"]`

### Requirement: 共享数据库文件
迁移容器 SHALL 挂载同一 `cp-data` 卷以访问 SQLite 数据库文件。

#### Scenario: 共享数据卷
- **WHEN** `migrate` 容器运行 efbundle
- **THEN** 数据库文件写入 `/app/data/mingocp.db`（通过 `ConnectionStrings__DefaultConnection` 配置）
- **THEN** `control-plane` 启动后可读取同一文件

### Requirement: 迁移幂等性
迁移 SHALL 仅应用未执行的迁移，多次运行安全。

#### Scenario: 重复执行迁移
- **WHEN** `migrate` 容器多次执行
- **THEN** 仅新的迁移会被应用
- **THEN** 已有数据不受影响
