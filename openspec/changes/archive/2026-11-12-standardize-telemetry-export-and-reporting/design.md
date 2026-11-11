## Context

阶段一修的是"数据面内存里的数字对不对"，阶段二修的是"标准 OTel 管道有没有真的产出"。本阶段修第三段：**这些数字怎么跨过进程边界，以及跨过去之后还剩下多少**。三段各自独立，前两段做完并不会让第三段变对——这正是它们能长期静默失效的原因。

勘察阶段已把事实逐条核到代码行（不是推测）：

| 位置 | 现状 | 后果 |
| --- | --- | --- |
| `HeartbeatReporter.cs` L119 | 每指标每 10s 只取最后 10 个点 | 控制面总数/错误率/分位全是抽样，100 rps 下抽样率约 1% |
| `HeartbeatReporter.cs` L83-86 | `TotalRequests` / `ErrorRequests` 取保留窗口求和 | 窗口滚动或触顶驱逐时数值往回走 |
| `HeartbeatReporter.cs` L91-95 | `CpuUsage=0`、`MemoryUsage=0`、`IsHealthy=true` | 实例列表永远 0% / 全绿 |
| `HeartbeatReporter.cs` L100 | `DrainAccessLogs(200)` + 队列 5000 FIFO | >20 rps 持续即丢日志，无计数 |
| `dataplane.proto` L92-100 | `AccessLogEntry` 字段止于 7 | 阶段二的 `TraceId` / `SpanId` 到控制面即消失 |
| `HeartbeatCollectService.cs` L69 | 写入 `instance_id` 标签 | 读取侧从不分组，多实例数据混算 |
| `TelemetryController.cs` L42-45 | `requestPoints.Count` 当请求总数 | 与 `MonitoringService` 的 `Sum` 两套口径 |
| `TelemetryController.cs` L62-65 | `Activity.Tags` 投影 | .NET 10 下 int/double 标签恒为 null |
| `GatewayConfig.cs` L122 | `MetricsEndpoint = "/metrics"` | 零读取方，是无实现者的承诺 |

一个对本阶段设计有决定性影响的实测事实来自阶段二：`GatewayTelemetryMiddleware.Record`（L98-110）写入的每个 `gateway.requests.total` 点 **Value 恒为 1**，`gateway.request.duration` 点 **Value 是该请求的毫秒数**。也就是说控制面 `MonitoringService` 的 `Sum` 与 `Average` 与 `NearestRank` 全部是"逐请求原始点"上的数学。这排除了任何"改成聚合上报"的方案（见 D1）。

## Goals / Non-Goals

**Goals**

- 心跳上报覆盖保留窗口内产生的**每一个**指标点；覆盖不到的部分变成可见计数，而不是无声抽样
- 心跳标量语义确定：请求累计 = 进程生命周期单调值；CPU / 内存 = 真实进程度量；`IsHealthy` = 真实配置同步状态
- 访问日志的链路关联键与代理维度经 gRPC 完整到达控制面，并在控制台可见
- 控制面读取侧只有一套计数口径，标签读取不再依赖 `Activity.Tags` 的字符串投影
- 把"标准化出口是什么"写成一个决定而不是一个悬念

**Non-Goals**

- 不上报 trace 样本本身（D6）：`Activity` 没有线上 schema，逐 span 序列化的代价与收益不成比例，链路数据的标准出口是 OTLP
- 不引入 OTel `MetricReader` / 聚合桥接，不改 `MonitoringService` 的逐点数学
- 不实现 `/metrics` Prometheus 端点，不新增 NuGet 包（尤其不引入 `System.Diagnostics.PerformanceCounter`）
- 不做控制面指标落库持久化（EF/SQLite 仍不存指标），不改心跳间隔与 10s 节律
- 不动容器编排层的健康检查（属在途的 `startup-dependency-healthcheck`；本阶段只复用其已经确立的判据）

## Decisions

**D1：原始点 + 游标增量抽取，而不是聚合上报。**

控制面的全部聚合数学建立在"收到的点=发生过的请求"之上（Value 恒为 1 的 total 点、Value 为毫秒的 duration 点）。改成 OTel 聚合导出会同时推翻存储语义、上报协议与 `MonitoringService` 三件事，且分位数在聚合桶里根本无法复现。因此保留逐点模型，只把"取最后 10 个"换成"续报上次没报完的"。

机制：`TelemetryStore` 在入队时给点盖一个**按指标名单调递增的序号**（`MetricPoint.Sequence`，仅数据面生产路径写入；控制面 ingest 自己构造的点保持 0，不参与游标）。新增 `ReadMetricPointsAfter(name, afterSequence, maxCount)`，返回序号大于游标的点，最旧优先，条数有界。`HeartbeatReporter` 每个指标名持有一个游标。

序号同时解决了"快照期间的驱逐"这个并发问题：`ConcurrentQueue` 的枚举器是取快照那一刻的视图，快照形成后头部被驱逐的点不会出现在视图里，于是它表现为**序号不连续**。抽取方据此算出 `首个序号 - 游标 - 1` 即"未上报就被本地驱逐"的条数，计入 `gateway.telemetry.unreported.dropped`。丢失变成可观测的数，而不是被 top-10 伪装成抽样。

代价与边界：单次抽取要枚举到目标位置，是 O(已上报点数)，但发生在每 10 秒的心跳路径上、不在每请求路径上，可接受（阶段一/二确立的"热路径禁用 `ConcurrentQueue.Count`"约束在这里不适用）。默认每指标每周期上限 2000 点（`Telemetry:MaxMetricPointsPerHeartbeat` 可覆盖）；指标名集合有界（3 个请求指标 + 自观测指标），单次心跳最坏约万点量级，仍在 gRPC 4 MB 消息预算内。超限不会丢数据，只是顺延到下个周期——**顺延与丢失是两件事，必须分开**。

**D2：游标在写入成功后才推进（at-least-once）。**

`BuildHeartbeatRequest` 先按游标取页、写流成功后才把游标推到页尾。写失败（断线、异常）时游标不动，下个周期重取同一页。

取舍：进程在"写完但没推游标"之间崩溃会重复上报最多一个周期。反过来（先推游标）就是本阶段要修的丢数据 bug。控制面对重复点的容忍度是既有事实——`AddMetric` 只做保留窗口裁剪，重复点会让总数偏高一个周期内的量，但不会破坏分位数与错误率的结构。缓解：日志在游标回退时留痕，且行为测试锁死"写失败不推进游标"。

重连（`ExecuteAsync` 外层重试循环）不清空游标：游标属于 store 状态而非流状态，进程活着就不该重复报送。

**D3：请求累计计数改用 `GatewayMetrics` 里的进程生命周期计数。**

`TotalRequests` 从"保留窗口内点值求和"改成 `Interlocked.Add` 的单调计数，与 OTel `Counter` 在同一个 `Record` 构造点里递增（L52-63）。选 `GatewayMetrics` 而不是 `TelemetryStore` 的理由：它是静态类，`OpenTelemetry:Enabled=false` 时中间件照样调用它，计数不受 OTel 开关影响；而放进 store 会诱导后人把它和保留窗口再次绑定——那正是当前 bug 的形状。

语义写明为"自数据面进程启动累计"，重启归零。控制面 `GatewayInstance.TotalRequests` 随之是"该实例自上次启动的累计"，`InstancesController` 与实例列表的展示口径不变。

**D4：系统指标经 `IProcessSampler` 取，不直接摸 `Process` / `Environment` 静态。**

CPU 用相邻两次 `Process.GetCurrentProcess().TotalProcessorTime` 之差除以 `Environment.ProcessorCount × 实际墙钟间隔`，钳到 [0,100]；内存用 `Environment.WorkingSet` 折算 MB（proto 里 `memory_usage` 是裸 double，语义必须在 spec 中钉死为 MB，否则前端"0.5"到底是 50% 还是 0.5 MB 无从判断）。

之所以包一层接口：`Process` 与 `Environment` 的静态调用无法喂假数据，而本阶段要第一次为心跳上报写行为测试。测试注入固定采样器，断言 `CpuUsage` 是按公式算出来的数而不是常量 0。

第一次心跳没有上一个采样点：用"进程启动以来的平均 CPU"作初值，避免首拍报 0 被误读成空闲。

**D5：`IsHealthy` = `DataPlaneConfigProvider.LastApplySucceeded`。**

不新造健康抽象。数据面对自身健康的唯一权威认知就是"配置是否同步成功"，而 `Program.cs` 的 `/healthz/ready` 已经用同一判据（L137 附近）。两处共用同一个属性，不会出现"就绪探针绿、心跳报红"的分裂。在途的 `startup-dependency-healthcheck` 负责端点与容器编排，本阶段只负责把它传给 `HeartbeatRequest.is_healthy`；两个变更在 `Program.cs` 上无重叠改动。

一个刻意保留的细节：预检失败的快照在 `ApplyConfig` 里提前返回（L58-63），`_lastApplySucceeded` 保持上一次成功值——因为旧配置仍在正常服务，此时把实例标成不健康会与就绪探针矛盾、也与代理实际跑的东西矛盾。因此本阶段把 `IsHealthy` 接到该属性时**不**改动 provider 的语义，spec 里也按"是否成功应用过配置"而不是"最后一次快照是否被接受"来写。

**D6：trace 样本不经心跳上报。**

选项与取舍：

- 逐 span 序列化上报：`Activity` 没有线上 schema，要自定义字段（名称、时间、父子 id、标签），控制面还得重建一棵树。代价与本阶段真正要解决的问题（日志↔链路可跳转）不成比例。
- 控制面查询时回源数据面：需要新增查询 RPC，属新协议而非修 bug。
- **采用**：链路数据的标准出口已经是 OTLP（阶段二起 `Gateway` 与 `Yarp.ReverseProxy` 两条 source 真的在导出），进程之间要传的只是**关联键**。因此只追加 `AccessLogEntry.trace_id` / `span_id`，跨进程链路查询仍不由控制面承诺，`GET /api/telemetry/traces` 在控制面读到的是本进程那个空 store——这条限制在阶段二的 spec 里已如实写明，本阶段继续如实保留，只把"关联键到达不了控制面"这半句改掉。

**D7：`AccessLogEntry` 追加字段采用"只增不改"。**

`dataplane.proto` L92-100 追加字段 8-11（`trace_id`、`span_id`、`cluster`、`destination`），已有 1-7 编号与类型一律不动。老数据面对接新控制面时这些字段是空串，控制面按"未知"处理而不是伪造哨兵值——`cluster` / `destination` 的 `none` 哨兵是**数据面采集侧**的契约，传输层不该替生产者编造取值。

Core 的 `AccessLogEntry` 同时补 `Cluster` / `Destination`，中间件用已经算好的同名局部变量填充（L84-85），与标签同源，不第二条派生路径。前端 TS 的 `AccessLog` 补 `traceId` / `spanId` / `cluster` / `destination`，并在阶段二遗留的 `p50/p95/p99`、`RequestMetrics.p95/p99` 上补齐展示。

**D8：口径统一走"值求和"，`instance_id` 变成可用维度。**

`/api/telemetry/stats` 的 `TotalRequests` / `ErrorRequests` 从 `queue.Count` 改为 `Sum(p => p.Value)`，与 `MonitoringService` 合流成一套口径；响应字段名不变，消费方无需改。阶段二之后每个 total 点值恰为 1，`Count` 与 `Sum` 数值暂时相等，因此这是消除隐患而不是修显错——但上报量修好之后，两者的差就是抽样率的差，必须在同一变更里落地，否则 `GetTagValue` 一修就露出两个自相矛盾的数。

`GetTagValue` 改用 `EnumerateTagObjects`（.NET 10 下 `Activity.Tags` 不返回 int/double 标签值，阶段二实测）。`instance_id` 提供可选过滤参数（`/api/telemetry/stats?instanceId=`、`/api/telemetry/metrics?instanceId=`），把"写了却没人读的标签"变成能读的维度；`MonitoringService` 的看板聚合仍跨实例，其口径在 spec 中写明，不悄悄改。

**D9：自观测指标是"累计值"，与逐请求点在控制面走不同数学。**

`gateway.telemetry.dropped`（本地驱逐）、新增 `gateway.telemetry.unreported.dropped`（未上报即被驱逐）、新增 `gateway.telemetry.access_log.dropped`（访问日志 FIFO 驱逐）三者的点值都是**累计计数**，不是每次新增量。它们的名字与"Value 恒为 1 的请求点"区分开，且绝不进入求和聚合——目前没有任何控制面查询按名字对它们求和，只有 `/api/telemetry/metrics` 的原始导出会看到。用三个独立指标名而不是给 `gateway.telemetry.dropped` 加 `kind` 标签：阶段一的 spec 明确写了它的标签集固定为空，加标签等于改掉那条已归档契约，收益却只是省一个名字。

**D10：标准化出口是 OTLP，`/metrics` 明确不做。**

`MonitoringConfig.MetricsEndpoint` 保留（删配置键会波及既有配置文件），但就地标注为未实现，并在 docs 的状态表里把"待阶段三"改成"决定不做"。仓库不引入任何 Prometheus 依赖。指标的第二条出口已由 D1 的心跳保证（控制面看板不需要抓取的端点），OTLP 保证给外部观测后端。

## 风险与缓解

| 风险 | 缓解 |
| --- | --- |
| 每周期点数上限造成积压后追不上（持续超过 2000 点/10s/指标） | 超限顺延而非丢弃，游标机制保证最终一致；`gateway.telemetry.unreported.dropped` 与积压在日志可见，运维可上调配置 |
| 控制面 ingest 吞吐上升（原来每指标 10 点，现在最多 2000 点） | ingest 只做 `AddMetric`，其裁剪已是记账计数；上限值保守并可配；本变更写清"上限是吞吐阀门" |
| 写完崩溃导致最多一个周期重复上报 | D2 的显式取舍，行为测试锁死游标推进时机 |
| 老数据面 × 新控制面（proto 新字段为空） | D7：按未知处理，不伪造哨兵值 |
| CPU 采样口径在不同 OS 上差异 | 用 `TotalProcessorTime`（托管跨平台）而非性能计数器；钳位 + 首拍均值兜底 |
| 与在途 `startup-dependency-healthcheck` 改动重叠 | D5 只读同一属性，不碰端点与 compose |
