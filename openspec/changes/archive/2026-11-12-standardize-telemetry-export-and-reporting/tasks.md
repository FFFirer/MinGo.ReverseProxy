## 1. 抽取层：让"每个点都能上报"成为存储的能力

- [x] 1.1 `TelemetryStore`：入队时给指标点盖按指标名单调递增的序号（`MetricPoint.Sequence`，默认 0；控制面 ingest 构造的点不依赖它）
- [x] 1.2 新增 `ReadMetricPointsAfter(name, afterSequence, maxCount)`：返回序号大于游标的点，最旧优先，条数有界；不在每请求路径调用
- [x] 1.3 访问日志 FIFO 驱逐改为可见：记账被丢弃条数，暴露 `DroppedAccessLogs`（与指标驱逐计数同一形状，判定仍用记账计数而非 `ConcurrentQueue.Count`）
- [x] 1.4 `TelemetryStore` 新增自观测指标名常量：`gateway.telemetry.unreported.dropped`、`gateway.telemetry.access_log.dropped`（固定空标签，与既有 `gateway.telemetry.dropped` 并列）

## 2. 心跳请求构造可测化（本仓库第一次给心跳写行为测试的前置）

- [x] 2.1 新增 `src/MinGo.DataPlane/Heartbeat/IProcessSampler` + `ProcessSampler`：`Sample()` 返回 CPU 百分比与内存 MB；时间窗由采样器内部用 `Stopwatch` 记，第一次采样用进程启动以来的平均 CPU
- [x] 2.2 新增 `HeartbeatRequestBuilder`：从 `HeartbeatReporter.BuildHeartbeatRequest`（L81-136）平移出来，依赖注入 `TelemetryStore` / `GatewayIdentity` / `IProcessSampler` / `DataPlaneConfigProvider` / 每指标上限
- [x] 2.3 `HeartbeatReporter` 改为委托 builder，并持有 per-metric 游标字典；写流成功后推进游标，失败不推进（design D2）
- [x] 2.4 上限配置读取：`Telemetry:MaxMetricPointsPerHeartbeat`（默认 2000），与 `Telemetry:MaxMetricPoints` 一样在 `Program.cs` 注册处解析

## 3. 消除 top-10 欠报

- [x] 3.1 删除 `Skip(Math.Max(0, pointsList.Count - 10))`，改为按游标取页；未上报即被驱逐的条数（首点序号与游标的差）记入 `gateway.telemetry.unreported.dropped`
- [x] 3.2 访问日志驱逐计数与指标驱逐计数作为累计值点随心跳上报（design D9：三个独立指标名，空标签）
- [x] 3.3 超过单周期上限时顺延到下个周期，不丢弃；日志留痕（游标未推进的量级）

## 4. 心跳标量的真实语义

- [x] 4.1 `GatewayMetrics.Record` 内用 `Interlocked` 累加请求总数与错误数，暴露为静态只读属性；与 `Counter.Add` 同一构造点，`OpenTelemetry:Enabled=false` 时照样累加（design D3）
- [x] 4.2 `HeartbeatRequest.TotalRequests` / `ErrorRequests` 改取生命周期累计值，不再取保留窗口求和
- [x] 4.3 `CpuUsage` / `MemoryUsage` 取 `IProcessSampler` 真实值；CPU 钳位 [0,100]，内存单位 MB（design D4）
- [x] 4.4 `IsHealthy` 取 `DataPlaneConfigProvider.LastApplySucceeded`，与 `/healthz/ready` 同判据（design D5）

## 5. 关联键跨进程贯通

- [x] 5.1 `dataplane.proto` 的 `AccessLogEntry` 追加字段 8-11：`trace_id` / `span_id` / `cluster` / `destination`（只增不改，1-7 保持原编号与类型）
- [x] 5.2 Core 的 `AccessLogEntry` 补 `Cluster` / `Destination`；中间件用已算好的同名局部变量填充（L84-85），与标签同源
- [x] 5.3 `HeartbeatRequestBuilder` 填充四个新字段
- [x] 5.4 `HeartbeatCollectService` 把新字段透传进控制面 store 的 `AccessLogEntry`；空串按"未知"处理，不伪造 `none`（design D7）
- [x] 5.5 控制面访问日志查询 DTO 输出 `traceId` / `spanId` / `cluster` / `destination`
- [x] 5.6 前端 TS `AccessLog` 补同名字段；`MetricsSummary` 补 `p50/p95/p99`、`RequestMetrics` 补 `p95/p99`
- [x] 5.7 控制台展示：监控页分位数（阶段二后端已就绪）与访问日志行的链路关联键

## 6. 控制面读取侧口径

- [x] 6.1 `TelemetryController.GetStats` 的 `TotalRequests` / `ErrorRequests` 从 `queue.Count` 改为对点值求和，与 `MonitoringService` 同一口径（design D8）
- [x] 6.2 `ActivityExtensions.GetTagValue` 改用 `EnumerateTagObjects`，使 `status`(int) / `duration`(double) 不再恒为 null
- [x] 6.3 `/api/telemetry/stats` 与 `/api/telemetry/metrics` 支持可选 `instanceId` 过滤，让 ingest 写入的 `instance_id` 成为可用维度
- [x] 6.4 在 spec 中写明 `MonitoringService` 看板聚合仍跨实例，避免"加了标签就等于按实例区分"的误解

## 7. 出口决策落地

- [x] 7.1 `GatewayConfig.MonitoringConfig.MetricsEndpoint` 就地标注为未实现（design D10），不删配置键
- [x] 7.2 `docs/基本可观测架构.md` 状态表更新：第七节（请求趋势）、第八节（按 cluster 分组）、第九节（关联键传输）、`/metrics` 行
- [x] 7.3 确认 OTLP 仍是 traces + metrics 的实际出口（阶段二已接通，本阶段不重复实现），docs 写明"标准化出口=OTLP，控制面看板=心跳"

## 8. 测试

- [x] 8.1 `TelemetryStoreTests`：序号单调、`ReadMetricPointsAfter` 的边界（游标等于最新序号、超过最新序号、上限截断）
- [x] 8.2 `TelemetryStoreTests`：访问日志驱逐计数在被 FIFO 丢弃时增长
- [x] 8.3 新增 `HeartbeatRequestBuilderTests`（首次覆盖心跳上报行为）：注入假 `IProcessSampler` 与预置 store，断言
      - 未上报的点**全部**进入请求，而非最后 10 个（构造 >10 点场景）
      - 第二次构建不重复已上报的点（游标推进）
      - 驱逐造成的序号缺口计入 `gateway.telemetry.unreported.dropped`
      - CPU / 内存来自采样器而非 0
      - `IsHealthy` 跟随 `LastApplySucceeded`
      - 单周期上限截断时数据不丢（顺延）
- [x] 8.4 关联键贯通测试：中间件 → store → builder 的 `trace_id` / `cluster` / `destination` 一致；控制面 ingest 重建条目保留四个字段
- [x] 8.5 `TelemetryController` 口径测试：多点值不为 1 的场景下 `Sum` 与 `Count` 可区分，断言取 `Sum`
- [x] 8.6 前端类型与后端 DTO 字段名一致性检查（人工核对 `MetricsSummary` / `RequestMetrics` / `AccessLog`）
- [x] 8.7 全量验证：`dotnet build` 零 error；`MinGo.Core.Tests`、`MinGo.DataPlane.Tests` 全绿；`GatewayTracingTests` 在并行宿主下仍通过（阶段二确立的锚点断言不受上报改造影响）

## 9. 验证结果（2026-11-12）

- 全解决方案 `dotnet build MinGo.ReverseProxy.slnx`：0 error（NU1902/NU1903 为仓库既有的传递依赖漏洞告警）
- `MinGo.Core.Tests` 107/107、`MinGo.DataPlane.Tests` 132/132、`MinGo.ControlPlane.Api.Tests`（新建）11/11 全绿
- 新增测试工程 `tests/MinGo.ControlPlane.Api.Tests`，并登记进 `MinGo.ReverseProxy.slnx` 的 `/tests/` 目录
- 8.3 落地为 `HeartbeatRequestBuilderTests` 14 例；期间抓出一个真实静默失效：
  `RecordEviction` 直接 `Enqueue` 绕过序号盖章，自观测驱逐点 `Sequence` 恒为 0，
  被游标读取永久过滤 —— 驱逐计数过去报不出去，将来也报不出去。
  已抽出 `TelemetryStore.Enqueue` 统一盖章，并由 `EvictionSelfObservingPoints_AlsoCarrySequences_...` 锁死
- 8.6 实测：后端 `MetricsSummary`/`RequestMetrics`/`AccessLog` 与前端 TS 同名字段一一对应（camelCase 序列化）；
  `npx tsc --noEmit` 对本阶段改动的 `types/index.ts`、`pages/Monitoring.tsx`、`pages/Logs.tsx` 零报错。
  前端另有 6 处**本阶段之前既有**的类型错误（Sidebar / Toast / VersionUpdateNotification / Certificates），
  与可观测性无关，未一并处理；`tsconfig.json` 的 project reference 缺 `composite` 使默认 `tsc -p` 直接失败，
  核对时用了临时配置绕开，仓库文件未改
