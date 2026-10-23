# portainer-cluster-import Specification

## Purpose
TBD - created by archiving change portainer-cluster-import. Update Purpose after archive.
## Requirements
### Requirement: Portainer 连接 SHALL 由配置驱动且只读

系统 SHALL 通过配置节 `Portainer`（`Enabled`、`BaseUrl`、`ApiKey`、`TimeoutSeconds`、`CacheSeconds`、`HostAddress`、`DataPlaneService`、`SelfStackNames`、`SelfImagePatterns`、`ExtraExcludedStacks`）连接 Portainer，SHALL 支持环境变量 `Portainer__BaseUrl` / `Portainer__ApiKey` 覆盖。系统 SHALL NOT 提供 Portainer 凭据的数据库存储、编辑或写回能力。

#### Scenario: 未启用时功能不可见
- **WHEN** `Portainer:Enabled` 为 `false` 或 `BaseUrl`/`ApiKey` 为空
- **THEN** `GET /api/portainer/discovery` SHALL 返回 503 及可读原因
- **AND** 前端"从 Portainer 导入"入口 SHALL 置灰并说明未配置
- **AND** cluster 手填流程 SHALL 不受影响

#### Scenario: Token 不回显
- **WHEN** 客户端请求 `GET /api/portainer/status`
- **THEN** 响应 SHALL 只包含 `enabled`、`baseUrl`（已去除 userinfo）、`reachable`、`version`、`endpointCount`、`apiKeyConfigured`、`selfDetection`、`error`
- **AND** SHALL NOT 包含 `ApiKey` 原文

#### Scenario: 连通性检测
- **WHEN** 客户端请求 `GET /api/portainer/status` 且 Portainer 可达
- **THEN** 系统 SHALL 调用 `GET /api/system/status` 并返回版本号

### Requirement: 发现 SHALL 以容器为准，不使用 /api/stacks 枚举应用

系统 SHALL 通过 `GET /api/endpoints`、`GET /api/endpoints/{id}/docker/containers/json?all=1`、`GET /api/endpoints/{id}/docker/networks` 完成发现。系统 SHALL 依据容器的 `com.docker.compose.project` 分组，SHALL NOT 依赖 `/api/stacks` 枚举应用。

#### Scenario: 未由 Portainer 创建的项目仍可见
- **WHEN** 某 compose 项目由命令行创建、不存在于 `/api/stacks`
- **THEN** 该项目 SHALL 仍然出现在发现结果中
- **AND** 其 `source` SHALL 为 `compose`

#### Scenario: 无 compose label 的容器
- **WHEN** 运行中容器没有 `com.docker.compose.project`
- **THEN** 该容器 SHALL 归入 `(未分组)` 分组且 `source` 为 `standalone`

#### Scenario: 网络归属来自容器侧
- **WHEN** `/api/endpoints/{id}/docker/networks` 的 `Containers` 映射为空
- **THEN** 系统 SHALL 使用容器的 `NetworkSettings.Networks` 判断网络归属

### Requirement: 地址推导 SHALL 提供容器名与宿主两种形态

系统 SHALL 为每个容器端口给出候选地址形态：容器名 `http://{containerName}:{privatePort}` 与宿主 `http://{host}:{publishedPort}`。系统 SHALL NOT 生成容器 IP 形态的地址。

#### Scenario: 容器名形态可用
- **WHEN** 目标网络为 user-defined bridge（名称不为 `bridge`）且该网络上存在 data-plane 容器
- **THEN** 响应 SHALL 提供 `addresses.containerName`

#### Scenario: 默认 bridge 禁用容器名
- **WHEN** 目标网络名称为 `bridge`
- **THEN** 响应 SHALL 不提供 `addresses.containerName`
- **AND** 前端 SHALL 禁用该形态并说明"默认 bridge 不支持容器名解析"

#### Scenario: data-plane 不在该网络
- **WHEN** 目标网络上没有属于自身 stack 且 `com.docker.compose.service` 等于 `DataPlaneService` 的运行中容器
- **THEN** 响应 SHALL 设置 `dataPlaneAttached=false`
- **AND** 前端 SHALL 禁用容器名形态并说明"data-plane 未接入此网络"

#### Scenario: 宿主形态使用具体绑定 IP
- **WHEN** 发布端口的绑定 IP 为具体地址（非 `0.0.0.0`、非 `::`）
- **THEN** `addresses.host` SHALL 使用该绑定 IP

#### Scenario: 宿主形态使用配置地址
- **WHEN** 发布端口绑定于 `0.0.0.0` 或 `::`
- **THEN** `addresses.host` SHALL 使用配置项 `HostAddress`

#### Scenario: 不生成容器 IP
- **WHEN** 任何容器同时具有 IP 与发布端口
- **THEN** 响应 SHALL 不包含以容器 IP 为 host 的候选地址

### Requirement: 系统 SHALL 自动识别自身 stack 并允许配置覆盖

系统 SHALL 按优先级确定自身 stack：配置 `SelfStackNames`（非空时）→ 容器主机名匹配容器 Id 前缀 → `SelfImagePatterns` 镜像匹配。识别结果 SHALL 在发现响应与状态响应中以 `self` / `selfDetection` 返回，包含是否命中、来源、命中的 stack 名称。

#### Scenario: 配置覆盖优先
- **WHEN** `SelfStackNames` 非空
- **THEN** 系统 SHALL 直接使用该列表作为自身 stack
- **AND** `self.source` SHALL 为 `config`

#### Scenario: 主机名匹配
- **WHEN** `SelfStackNames` 为空且运行环境主机名等于某容器 Id 的前 12 位
- **THEN** 系统 SHALL 将该容器的 `com.docker.compose.project` 作为自身 stack
- **AND** `self.source` SHALL 为 `hostname`

#### Scenario: 主机名未命中
- **WHEN** 主机名不能匹配任何容器（例如控制面未部署在目标 Portainer 所管理的环境中）
- **THEN** `self.detected` SHALL 为 `false`
- **AND** 系统 SHALL NOT 排除任何 stack
- **AND** 前端 SHALL 显示告警并提示可通过 `SelfStackNames` 显式指定

#### Scenario: 镜像兜底
- **WHEN** 主机名未命中且存在镜像匹配 `SelfImagePatterns` 的容器
- **THEN** 系统 SHALL 取这些容器的 compose project 作为自身 stack
- **AND** `self.source` SHALL 为 `image`

### Requirement: 自我排除 SHALL 生效并可见

系统 SHALL 在发现结果中排除非运行容器、自身 stack 的容器、带 `io.portainer.server=true` 或 `io.portainer.agent=true` 标签的系统容器、以及 `ExtraExcludedStacks` 中的 stack，并在 `excluded[]` 中返回被排除项与原因。

#### Scenario: 排除自身控制面与数据面
- **WHEN** 存在属于自身 stack 的容器
- **THEN** 该容器 SHALL NOT 出现在可勾选列表
- **AND** `excluded[]` SHALL 包含其名称与原因 `self-stack`

#### Scenario: 排除退出态容器
- **WHEN** 容器 `State` 不为 `running`
- **THEN** 该容器 SHALL NOT 出现在可勾选列表
- **AND** 原因 SHALL 为 `not-running`

#### Scenario: 按标签排除 Portainer 系统容器
- **WHEN** 容器带 `io.portainer.server=true` 或 `io.portainer.agent=true`
- **THEN** 该容器 SHALL NOT 出现在可勾选列表
- **AND** 原因 SHALL 为 `portainer-system`

#### Scenario: 额外排除
- **WHEN** 容器的 compose project 属于 `ExtraExcludedStacks`
- **THEN** 该容器 SHALL NOT 出现在可勾选列表
- **AND** 原因 SHALL 为 `extra-excluded`

### Requirement: 导入 SHALL 经人工确认并复用既有 cluster API

系统 SHALL NOT 提供独立的 Persist/Import 写端点。前端 SHALL 在用户确认预览后，通过既有 `POST /api/apimanagement/clusters`（新建）或 `GET` + 合并 + `PUT /api/apimanagement/clusters/{id}`（追加）完成写入。

#### Scenario: 新建 cluster
- **WHEN** 用户勾选若干 容器×端口 并选择"新建 cluster"确认
- **THEN** 前端 SHALL 提交包含所选地址的 `POST /api/apimanagement/clusters`
- **AND** destination 的 `id` SHALL 由既有后端逻辑自动生成

#### Scenario: 追加到已有 cluster
- **WHEN** 用户选择追加到已有 cluster
- **THEN** 前端 SHALL 先读取该 cluster，保留已有 destination 的 `id`，仅追加新地址后提交 `PUT`
- **AND** 已存在的相同地址 SHALL 被去重跳过

#### Scenario: 名称冲突
- **WHEN** 新建 cluster 的名称已存在导致 `POST` 返回 409
- **THEN** 前端 SHALL 提示重名并聚焦名称输入框，SHALL NOT 自动改名

### Requirement: 预览 SHALL 可编辑地址与名称

预览阶段 SHALL 允许用户编辑 cluster 名称、每条 destination 的最终地址，并可取消任意勾选项。

#### Scenario: 编辑地址后提交
- **WHEN** 用户在预览中把手改某条地址
- **THEN** 提交的该条 destination SHALL 使用用户编辑后的地址

#### Scenario: 集群名预填
- **WHEN** 用户选择某个 compose 分组新建 cluster
- **THEN** 名称 SHALL 预填为分组名的 slug（小写、空白转 `-`、仅保留 `[a-z0-9._-]`）
- **AND** 用户 SHALL 可以在提交前修改

### Requirement: 发现 SHALL 具备缓存与降级

系统 SHALL 缓存发现结果 `CacheSeconds`（默认 30 秒），单 endpoint 失败 SHALL NOT 影响其他 endpoint。

#### Scenario: 缓存命中
- **WHEN** 30 秒内重复请求发现接口且 `endpointId` 相同
- **THEN** 系统 SHALL 返回缓存结果，不重复调用 Portainer

#### Scenario: 单 endpoint 失败
- **WHEN** 某个 endpoint 的容器或网络请求失败
- **THEN** 该 endpoint SHALL 标记 `error` 并携带原因
- **AND** 其他 endpoint SHALL 正常返回

