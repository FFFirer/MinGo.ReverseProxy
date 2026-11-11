## Why

阶段一让内存 `TelemetryStore` 的数字变正确，阶段二让 OTel 的两条标准管道开始真实产出。但**数据面到控制面的那段传输仍然是错的**，而且同样是静默失效——没有编译错误，没有测试会红。

**心跳指标上报是系统性欠报。** `HeartbeatReporter.cs` L119 每个指标每 10 秒只取最后 10 个点：

```csharp
foreach (var point in pointsList.Skip(Math.Max(0, pointsList.Count - 10)))
```

控制面 `MonitoringService` 的全部数学（对逐请求点求和算总数、求平均、取分位）都建立在"收到的点=发生过的请求"这个假设上，而这个假设只在流量低于 1 点/秒时成立。100 rps 时每个心跳周期实际产生 ~1000 个点，只上报 10 个，控制面看到的总数、错误率、P95 全都是 1% 的抽样——且抽样偏向"最近 10 个请求"，错误率会因为一次故障尾巴而被放大或被稀释。[docs/基本可观测架构.md](../../docs/基本可观测架构.md) 第七、八节把请求趋势与按 cluster 分组统计标为"待阶段三"，根因就在这行。

**心跳的四个标量是硬编码常量。** `HeartbeatReporter.cs` L91-95：`CpuUsage = 0`、`MemoryUsage = 0`、`IsHealthy = true`。`GatewayInstanceService.UpdateHeartbeatAsync`（L63-68）忠实地把这些值写进实例对象，控制台因此永远显示 CPU 0%、内存 0%、所有实例健康。`TotalRequests` / `ErrorRequests` 取的是 24 小时保留窗口的求和——窗口滚动或触发 50000 点上限驱逐时，这个数会**往回走**，而它被存成实例的累计计数器。

**关联键传到控制面就丢了。** 阶段二给 `AccessLogEntry` 加了 `TraceId` / `SpanId`，但 `dataplane.proto` L92-100 的 `AccessLogEntry` 只有字段 1-7（timestamp / method / path / status_code / duration_ms / client_ip / route），`HeartbeatCollectService.cs` L82-92 重建条目时也没有这两个字段。结果：控制面"最近请求列表"里的一条 5xx 无法跳到链路——阶段二建立的桥在进程边界被截断。

**访问日志的丢弃不可见。** `DrainAccessLogs(200)` 每 10 秒最多取 200 条，队列上限 5000 且 FIFO 静默驱逐（`TelemetryStore.AddAccessLog` L219-227）。持续 20 rps 以上，积压必然触顶，日志在数据面被丢掉而没有任何计数——阶段一为指标点补的"驱逐可见"（`gateway.telemetry.dropped`）没有覆盖访问日志。

**控制面读取侧有两处口径错误。** `TelemetryController.cs` L62-65 的 `GetTagValue` 走 `Activity.Tags`，而 .NET 10 下该投影只给出字符串形态标签值，`status`(int) 与 `duration`(double) 恒为 null（阶段二实测确认，已在 docs 记录为遗留限制）；同一文件 L42-45 的 `/api/telemetry/stats` 用**队列点数**（`requestPoints.Count`）当请求总数，而 `MonitoringService` 用**值求和**，两套口径并存——在阶段二之后每个点仍只代表 1 次请求时两者恰好接近，一旦补上真实上报，`Count` 与 `Sum` 的差别就是"抽样率"的差别。`HeartbeatCollectService.cs` L69 给每个点加了 `instance_id` 标签，但读取侧从不按它分组，多实例数据在控制面被无区分地混算。

**`MetricsEndpoint = "/metrics"` 是一个没有实现者的配置承诺。** `GatewayConfig.cs` L122 定义、全仓库零读取方（`grep MetricsEndpoint` 仅命中定义处），且无 Prometheus 依赖。留着它等于让运维以为有 `/metrics` 可抓。

## What Changes

- **增量游标上报**：`TelemetryStore` 为每个指标名维护单调序号与驱逐记账，新增按游标读取的接口；`HeartbeatReporter` 为每个指标名记住"报到哪儿了"，每周期续报未上报的点，单指标每周期上限可配（默认 2000 点）。上报前就被本地保留窗口驱逐的点计入新的自观测指标 `gateway.telemetry.unreported.dropped`——欠报变成可见的数，而不是抽样率的伪装
- **心跳标量改单调累计**：`TotalRequests` / `ErrorRequests` 改为进程生命周期累计计数（`GatewayMetrics` 里的 `Interlocked` 计数，与 OTel instrument 同一构造点，`OpenTelemetry:Enabled=false` 时照样累加），不再随保留窗口回退
- **真实系统指标**：`CpuUsage` 用 `Process.TotalProcessorTime` 在心跳间隔内的增量除以 `Environment.ProcessorCount × 间隔`，`MemoryUsage` 用 `Environment.WorkingSet` 折算 MB；不新增 NuGet 包（不用 `PerformanceCounter`）
- **真实健康状态**：`IsHealthy` 由配置同步状态推导（收到过快照且最后一次 apply 成功），与在途的 `startup-dependency-healthcheck` 变更的 `/healthz/*` 端点语义一致但不重复实现容器编排部分
- **关联键贯通**：`dataplane.proto` 的 `AccessLogEntry` 追加 `trace_id` / `span_id` / `cluster` / `destination`（字段 8-11，仅追加，不改动已有字段编号），控制面 ingest 与 REST DTO 一并透传，前端 TS 类型同步
- **访问日志丢弃可见**：`TelemetryStore` 记录 FIFO 驱逐条数并暴露，心跳把它作为一条 `gateway.telemetry.unreported.dropped` 点上报
- **控制面口径统一**：`/api/telemetry/stats` 改为与 `MonitoringService` 同一口径（对点值求和），`GetTagValue` 改用 `EnumerateTagObjects`，`instance_id` 成为可分组维度
- **出口决策写明**：OTLP 是唯一的标准化出口（traces/metrics/logs 三路），`/metrics` 明确不实现；`MonitoringConfig.MetricsEndpoint` 就地标注为未兑现配置，不再被当作待办承诺
- **前端补齐展示**：`MetricsSummary` 的 `p50/p95/p99` 与 `RequestMetrics` 的 `p95/p99` 补进 TS 类型并在监控页展示（阶段二后端已就绪，属遗留的最后一公里），访问日志行新增链路关联键

## Capabilities

### New Capabilities

- `telemetry-reporting`: 数据面到控制面的上报契约——指标增量抽取与游标推进、每周期上限、未上报丢弃的可见性、心跳标量的累计语义、系统指标与健康状态的来源、访问日志跨进程的字段完整性、控制面聚合口径的唯一性

### Modified Capabilities

- `request-tracing`: 关联键必须穿越进程边界——访问日志上报保留 `trace_id` / `span_id`；同时明确 trace 样本本身不经 gRPC 上报，跨进程链路查询仍不由控制面承诺（标准化出口是 OTLP）
- `request-telemetry`: 保留窗口的驱逐可见性从指标点扩展到访问日志；上报周期内的抽取不再依赖 `ConcurrentQueue` 的下标遍历

## Impact

- **`src/MinGo.Core/Services/TelemetryStore.cs`**：指标序号与驱逐记账、按游标读取接口、访问日志驱逐计数
- **`src/MinGo.DataPlane/Heartbeat/`**：新增 `HeartbeatRequestBuilder.cs`（把请求构造从 `BackgroundService` 里抽出，首次可测）、新增 `IProcessSampler` / `ProcessSampler`（CPU、内存的真实取值与可注入替身）；`HeartbeatReporter.cs` 改为委托构造 + 维护游标
- **`src/MinGo.DataPlane/Telemetry/GatewayMetrics.cs`**：生命周期累计计数
- **`src/MinGo.ControlPlane.Api/GrpcServices/Protos/dataplane.proto`**：`AccessLogEntry` 追加字段 8-11（仅追加，向后兼容）
- **`src/MinGo.ControlPlane.Api/GrpcServices/HeartbeatCollectService.cs`** + **`Controllers/TelemetryController.cs`**：新字段透传、口径统一、标签读取修正
- **`src/MinGo.Core/Services/TelemetryStore.cs` 的 `AccessLogEntry`**：补 `Cluster` / `Destination`（内存模型已有 `Route`，维度对齐）
- **`src/MinGo.Core/Models/GatewayConfig.cs`**：`MetricsEndpoint` 标注 inert；新增 `Telemetry:MaxMetricPointsPerHeartbeat` 配置读取
- **`frontend/min-go-console/src/types/index.ts` + `Monitoring.tsx`**：分位数字段与展示、访问日志关联键
- **`docs/基本可观测架构.md`**：第七、八、`/metrics` 三行状态更新
- **依赖**：不新增 NuGet 包；proto 仅追加字段，不引入破坏性变更
- **不在本变更范围**：trace 样本的跨进程传输与 `GET /api/telemetry/traces` 返回真实数据（理由见 design D6——Activity 没有线上 schema，且 OTLP 已覆盖该需求）；控制面指标落库持久化（EF/SQLite 仍不存指标，属另一主题）；Prometheus 抓取端点
