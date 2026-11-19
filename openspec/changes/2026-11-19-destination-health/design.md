## Context

"让健康检查真实有效"落到这套架构上要回答四个问题：探测放在哪一层；真实状态怎么跨进程回传；多数据面状态怎么聚合；UI 的口径什么时候从"配置标记"切换成"运行态"。

## Decisions

### 1. 探测放数据面，用 YARP 原生健康检查，控制面不造探测服务

转发决策发生在数据面进程内，摘除/恢复端点必须是转发器的行为，否则"UI 说不健康"与"流量还往那打"会永远分叉。YARP 的 Active/Passive HealthCheck 就是这条路的标准做法（对标 Nginx active/passive、Envoy active health check）。控制面另起 HttpClient 探测只能供展示、不改转发，是重复造轮子。

`TranslateConfig` 把快照的 `health_check_json` 反序列化为 `HealthCheckConfig` 后翻译：

| 配置模型 | YARP 2.3 目标 |
| --- | --- |
| `Active.Enabled/Interval/Timeout/Path` | `ActiveHealthCheckConfig` 同名字段 |
| `Active.ExpectedStatusCodes`（非空） | `ActiveHealthCheckConfig.Response.ExpectedStatusCodes` |
| `Active.ExpectedStatusCodes` 为空 | 不设置 `Response`（YARP 默认口径：非 5xx 即成功） |
| `Passive.Enabled/ReactivationPeriod` | `PassiveHealthCheckConfig` 同名字段 |
| `Passive.Policy` | 白名单映射：`FailureRate`/`TransportFailureRate` 直通，其余回退不设置（YARP 默认 `FailureRate`）并记 Warning |
| `DestinationConfig.Healthy` | `DestinationConfig.Health`（初始态，重启后继承上次已知状态直到首轮探测完成） |

语义变化要写明：配置模型默认 `ExpectedStatusCodes = [200]`，接线后探测成功口径从"非 5xx"变为"仅 200"——这正是配置模型的既定意图，不是回归。被动策略 `ConsecutiveFailures` 不是 YARP 合法被动策略，翻译时回退默认并记 Warning，**不进** `ValidateSnapshot` 拒绝整份快照（那会让全部路由 404，代价远大于一个探测策略回退）。

### 2. 回传走心跳，折叠成布尔，不扩展枚举

`HeartbeatRequest` 追加 `repeated DestinationHealth destination_health = 10`（`cluster_id=1, destination_id=2, is_healthy=3`），遵循 proto 既有"追加不重编"约定。心跳周期 10 秒，状态延迟可接受；控制面已有的心跳通道承载它，不新建 RPC。

数据面从 YARP 按集群注册的 keyed `IClusterDestinationsState` 读 `AllDestinationsHealth`，每端点折叠：

- Active 或 Passive 任一为 `Unhealthy` → `is_healthy = false`
- 其余（含 `Unknown`）→ `true`。`Unknown` 视为健康与 YARP 转发行为一致：未配置检查或首轮探测未完成的端点仍参与转发。

无健康检查配置的集群：状态恒 `Unknown`，折叠为 `true`，与现状（恒在线）一致。

### 3. 控制面聚合口径：任一判不健康即不健康，仅变化才落库

同一端点被多个数据面各自探测，网络分区下结论可能不同。取 fail-closed 聚合：任一数据面报告不健康 → 落库 `false`；全部报告健康 → `true`；无任何数据面报告该端点 → 保持现值。聚合器为控制面单例（`Dictionary<(cluster, dest), Dictionary<dpId, bool>>`），心跳更新该实例条目并重算聚合，只有聚合值变化才写库（心跳 10s × 实例数的写放大被压到状态翻转次数）；数据面断连时在现有 `Disconnect` 路径清理其条目。心跳引用的集群/端点已不存在时忽略。

### 4. 先 spike 实测 YARP API，不凭记忆断言

按仓库惯例（route-domain-dedupe 的 `YarpMatchPrecedenceTests` 先例），任务 1 是 spike 测试锁定：主动检查开箱可用性（默认 `IProbingRequestFactory` 注册）、keyed `IClusterDestinationsState` 的解析方式与枚举取值、被动策略名的配置校验行为、`Response.ExpectedStatusCodes` 空列表语义。决策 1/2 中任何一条被实测推翻，先改设计再写实现。

### 5. UI 语义切换，但不动写路径

`ApiDbService` 三处强制 `Healthy = true` 的写路径保留：编辑集群把端点重置为健康，下一拍心跳（≤10s + 探测间隔）即纠正，可接受且避免本次改动面扩大。集群页"在线/离线"徽章数据变真实后文案成立；拓扑页端点文案"配置健康"→"运行健康"，图注改为说明状态来自数据面心跳回传、延迟为心跳周期级。被动策略下拉选项改为 `FailureRate`/`TransportFailureRate`。

## Risks

- **YARP API 与记忆不符**：`Response.ExpectedStatusCodes`、keyed `IClusterDestinationsState` 的可用性均以 spike 实测为准；不符则按实测改设计（例如改用 `IDestinationHealthUpdater` 或自定义 `IProbingRequestFactory`）。
- **探测流量打到后端**：主动检查按配置的 `Path` 定期请求每个端点，可能干扰没有健康端点的后端。缓解：这是配置既有意图（用户显式开启主动检查），间隔/路径可调；`/healthz` 探测路径已被遥测中间件排除，不污染指标。
- **状态抖动**：探测短暂失败会让端点状态翻转，控制面落库随之翻转。缓解：YARP 主动检查默认 `ConsecutiveFailures` 策略（连续失败才摘除），被动检查有 `ReactivationPeriod`；控制面只展示不参与转发，抖动不影响流量。
- **心跳字段被老控制面忽略**：新数据面 + 老控制面时状态不回传，UI 退回现状（恒健康），无破坏。
