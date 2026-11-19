## Why

集群"健康检查"目前是幽灵功能：控制面存着主动/被动检查配置（间隔、路径、期望状态码、策略），表单和列表还展示"健康检查"徽章与端点"在线/离线"状态，但整条链路没有任何探测在执行——数据面把快照翻译成 YARP 集群时只填 `Address`，`HealthCheckJson` 与 `dest.Healthy` 全部丢弃；`ApiDestinationEntity.Healthy` 在所有写路径被强制写 `true`，无任何自动或手动更新通道；`CONFIG_REPORT` 里 `Healthy` 更是硬编码 `true`。结果是后端进程宕机、端口不通，控制台仍显示"在线/健康"，运维被 UI 误导，坏端点永远参与转发。

## What Changes

- **数据面执行真实健康检查**：`TranslateConfig` 把快照里的 `health_check_json` 翻译成 YARP `HealthCheckConfig`（主动检查 `Interval/Timeout/Path/ExpectedStatusCodes`、被动检查 `Policy/ReactivationPeriod`），`dest.Healthy` 作为端点初始健康态。转发层的摘除/恢复由 YARP 运行时决定，控制面不参与转发决策。
- **心跳回传真实状态**：`HeartbeatRequest` 追加 `destination_health` 字段（proto 追加新编号，不重编旧字段）；数据面从 YARP 的 `IClusterDestinationsState` 读取每端点运行状态，折叠成 `(clusterId, destinationId, isHealthy)` 随 10 秒心跳上报。
- **控制面聚合落库**：新增 `DestinationHealthAggregator` 单例，按"任一数据面判不健康即不健康"（fail-closed）聚合多实例状态，仅值变化时写回 `ApiDestinationEntity.Healthy`；数据面断连时清理其条目。
- **UI 与拓扑切换到运行态口径**：集群页"在线/离线"徽章、拓扑页端点状态随之变为真实探测结果（拓扑文案从"配置健康"改为"运行健康"，图注说明心跳延迟）；被动策略下拉改为 YARP 实际支持的 `FailureRate`/`TransportFailureRate`（现有 `ConsecutiveFailures` 不是合法被动策略）。
- **`CONFIG_REPORT` 反映真实状态**：`GetConfigSnapshotJson` 不再硬编码 `Healthy = true`，改读运行时状态。

## Capabilities

### New Capabilities

- `destination-health`: 端点健康检查的执行（数据面 YARP 主动/被动）、心跳回传、控制面聚合落库与 UI 呈现口径。

### Modified Capabilities

- `grpc-config-replication`: 快照的 `health_check_json` 与 `dest.healthy` 被数据面翻译为 YARP 健康检查配置与初始健康态；非法被动策略回退默认并记 Warning（不拒绝整份快照）。
- `network-topology-view`: 端点健康从"配置标记"切换为心跳回传的运行态，文案与图注同步更新。
- `instance-config-query`: `CONFIG_REPORT` 的 `destinations.healthy` 反映数据面运行时状态而非硬编码。
- `telemetry-reporting`: 心跳新增目标健康段，控制面"仅变化才落库"的写入语义。

## Impact

- **MinGo.Core**：无改动（`HealthCheckConfig` 模型与 `DestinationConfig.Healthy` 已存在，直接复用）。
- **MinGo.ControlPlane.Api**：`dataplane.proto` 追加字段；新增 `DestinationHealthAggregator`；`HeartbeatCollectService` 接入聚合器；`ApiDbService` 新增仅改 `Healthy/UpdatedAt` 的更新方法；`TopologyService` 文案。
- **MinGo.DataPlane**：`TranslateConfig` 接线 YARP 健康检查；`GetConfigSnapshotJson` 读运行时状态；`HeartbeatRequestBuilder` 追加目标健康段（状态读取抽成可测接口）；注册依赖微调。
- **frontend/min-go-console**：`Clusters.tsx` 被动策略下拉选项、`Topology.tsx` 图注文本。
- **兼容性**：无数据库迁移（`Healthy` 列已存在）；proto 追加字段，老数据面不填即"未传输"，控制面保持现值；老控制面忽略新字段。
- **非目标**：不在控制面另造探测服务；不做健康状态历史/趋势存储（已有 OTLP 指标出口）；不加手动"标记健康/不健康"入口；不做告警通知；不改 `IngressReconciler` 与 Cloudflare 隧道侧逻辑。
