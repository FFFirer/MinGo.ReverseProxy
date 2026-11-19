## ADDED Requirements

### Requirement: 数据面 SHALL 把健康检查配置翻译为 YARP 健康检查并生效

数据面把快照翻译为 YARP 集群时 SHALL 解析 `health_check_json` 并设置 `HealthCheck`：主动检查的 `Enabled/Interval/Timeout/Path` 直通，`ExpectedStatusCodes` 非空时设置到 `Response.ExpectedStatusCodes`；被动检查的 `Enabled/ReactivationPeriod` 直通，`Policy` 仅接受 `FailureRate` 与 `TransportFailureRate`，其余值回退为不设置（YARP 默认策略）并记 Warning，SHALL NOT 因此拒绝整份快照。`DestinationConfig.Healthy` SHALL 作为该端点的初始健康态（`DestinationConfig.Health`），使数据面重启后继承上次已知状态直到首轮探测完成。

#### Scenario: 主动检查配置生效

- **WHEN** 快照中某集群的 `health_check_json` 启用主动检查（间隔、超时、路径、期望状态码 200）
- **THEN** 数据面生成带 `ActiveHealthCheckConfig` 的 YARP 集群，YARP 按配置周期向各端点 `Path` 发探测请求
- **AND** 探测返回状态码不在期望列表内的端点被 YARP 摘除转发

#### Scenario: 停用检查时不探测

- **WHEN** 集群的主动与被动检查均未启用
- **THEN** 数据面不设置 YARP `HealthCheck`，端点状态恒为 `Unknown` 且全部参与转发

#### Scenario: 非法被动策略回退而非拒绝

- **WHEN** `health_check_json` 的被动策略为 `ConsecutiveFailures`（非 YARP 合法值）
- **THEN** 数据面按默认策略运行、记录 Warning，快照其余部分照常生效

#### Scenario: 端点初始健康态透传

- **WHEN** 快照中某端点 `healthy = false`
- **THEN** 数据面把该端点初始状态设为不健康，直到主动/被动检查将其恢复

### Requirement: 数据面 SHALL 在心跳中携带每个集群端点的折叠健康状态

数据面 SHALL 从 YARP 运行时状态（`IClusterDestinationsState`）读取每个集群端点的健康状态，折叠为布尔后随心跳上报：Active 或 Passive 任一为 `Unhealthy` 折叠为不健康，其余（含 `Unknown`）折叠为健康，与 YARP 转发行为一致。心跳协议通过 `HeartbeatRequest.destination_health` 追加字段承载（proto 追加不重编）。

#### Scenario: 心跳携带目标健康段

- **WHEN** 数据面构建心跳请求
- **THEN** `destination_health` 含每个集群每个端点的一条 `(cluster_id, destination_id, is_healthy)`
- **AND** 折叠结果来自 YARP 运行时状态而非配置值

#### Scenario: 未配置健康检查的集群恒报健康

- **WHEN** 某集群未启用任何健康检查
- **THEN** 其端点状态为 `Unknown`，折叠后上报 `is_healthy = true`

### Requirement: 控制面 SHALL 聚合多数据面状态并仅在变化时落库

控制面 SHALL 按 fail-closed 口径聚合各数据面上报的端点状态：任一数据面判不健康则落库不健康；全部报告健康则落库健康；无任何数据面报告该端点则保持现值。聚合结果 SHALL 仅在相对库内现值变化时写库。数据面断连时 SHALL 清理其上报条目。心跳引用不存在的集群或端点时 SHALL 忽略。

#### Scenario: 多实例聚合任一不健康即不健康

- **WHEN** 两个数据面探测同一端点，一个上报健康、一个上报不健康
- **THEN** 控制面落库该端点为不健康

#### Scenario: 状态翻转才写库

- **WHEN** 心跳携带的聚合结果与库内现值相同
- **THEN** 控制面不发起数据库写

#### Scenario: 数据面断连后其条目被清理

- **WHEN** 数据面断开 gRPC 连接
- **THEN** 该数据面所有端点状态条目被移除，后续聚合不再计入它

### Requirement: 端点健康 SHALL 以运行态呈现于 UI 与拓扑

集群页端点的"在线/离线"徽章与拓扑页端点状态 SHALL 基于心跳回传的落库值（运行态），延迟为心跳周期级。拓扑页端点详情 SHALL 标注"运行健康"，页面图注 SHALL 说明状态来源与延迟。

#### Scenario: 集群页展示真实状态

- **WHEN** 数据面探测到某端点连续失败并上报
- **THEN** 集群页该端点徽章显示"离线"，集群徽章显示"异常"

#### Scenario: 拓扑页口径与图注

- **WHEN** 用户查看拓扑页端点节点
- **THEN** 详情字段为"运行健康/运行不健康"，图注说明状态来自数据面探测并经心跳回传、延迟为心跳周期级

### Requirement: CONFIG_REPORT SHALL 反映数据面实时端点状态

数据面响应 `CONFIG_QUERY` 的集群快照中，`destinations[].healthy` SHALL 取 YARP 运行时状态折叠结果，SHALL NOT 硬编码为 `true`。

#### Scenario: 运行时状态进入配置报告

- **WHEN** 数据面某端点当前被 YARP 判定为不健康
- **THEN** `CONFIG_REPORT` 中该端点的 `healthy` 为 `false`
