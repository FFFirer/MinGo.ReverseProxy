## 1. OTel instruments（修复空壳出口）

- [x] 1.1 新增 `src/MinGo.DataPlane/Telemetry/GatewayMetrics.cs`：静态 `Meter("Gateway")` 上创建 `gateway.requests.total` / `gateway.requests.errors` Counter 与 `gateway.request.duration` Histogram
- [x] 1.2 instruments 与 `TelemetryStore` 的标签来自同一构造点（中间件里一次性构造，禁止两处各自拼标签）
- [x] 1.3 命名常量集中：`GatewaySourceName` / `YarpSourceName` / `MeterName` 定义在单处，Program.cs 与测试共用
      额外：三个指标名也提为常量（`RequestsTotalName` 等），中间件写 store 时复用，避免两条出口的字符串各写一遍

## 2. 追踪配置单点化（避免生产配置与测试配置漂移）

- [x] 2.1 新增 `src/MinGo.DataPlane/Telemetry/GatewayOpenTelemetry.cs`：`AddGatewayTelemetry(otlpEndpoint, store)` 扩展，同时装配 tracing（含本地 processor）与 metrics
- [x] 2.2 `Program.cs` 改为调用该扩展 —— **原计划的 `WithTracing((sp, b) => ...)` 重载在 1.15.3 不存在（CS1593）**，改用 `AddProcessor(Func<IServiceProvider, BaseProcessor<Activity>>)` 解析 `TelemetryStore`，见 design D4
- [x] 2.3 tracing 订阅 `Gateway` + `Yarp.ReverseProxy`（名称以运行时实测为准：`LISTEN_CANDIDATE Yarp.ReverseProxy`）
      验证方式改为 E2E：`GatewayTracingTests` 断言 `proxy.forwarder` span 真的进入 store，名字写错会直接失败

## 3. 本地 trace 落地

- [x] 3.1 新增 `src/MinGo.DataPlane/Telemetry/LocalTraceExporter.cs`：`BaseExporter<Activity>` 写入 `TelemetryStore.AddTrace`
- [x] 3.2 `AddTrace` 改用记账计数替代 O(n) 的 `ConcurrentQueue.Count`（design D3 的前置条件）
- [x] 3.3 `TelemetryStore.Traces` 上限行为可测（超上限驱逐最旧）；`MaxTraces` 由 private 提升为 public const 供测试引用

## 4. 日志与链路关联

- [x] 4.1 `AccessLogEntry` 增加 `TraceId` / `SpanId`（仅内存模型，不动 proto；proto 与上报属阶段三）
- [x] 4.2 中间件写入：有 Activity 用 W3C TraceId/SpanId；无 Activity 回退 `ctx.TraceIdentifier`（design D5）
- [x] 4.3 Activity 补充语义属性：`http.request.method`、`url.path`、`http.response.status_code`

## 5. 总览分位数

- [x] 5.1 在 `MinGo.Core` 新增分位计算工具（nearest-rank），供 `MonitoringService` 与总览复用同一实现
- [x] 5.2 `MetricsSummary` 增加 `P50ResponseTime` / `P95ResponseTime` / `P99ResponseTime`
- [x] 5.3 `MonitoringService.GetMetricsSummaryAsync` 填充分位数，`RequestMetrics` 改用同一工具（去掉重复私有实现）

## 6. 测试

- [x] 6.1 `GatewayTelemetryMiddlewareTests`：访问日志的 `TraceId` 非空且在两次请求间不同（无 listener 时的 `TraceIdentifier` 回退）
      补充断言：无 Activity 时 `SpanId` 保持 null，不把 `TraceIdentifier` 伪造成 W3C span id
- [x] 6.2 新增 `GatewayTracingTests`：用 `GatewayOpenTelemetry` 装配真实 OTel + 本地 exporter，代理请求后断言 `Traces` 同时含 `Gateway` 与 `Yarp.ReverseProxy` 的 span，且 destination 属性存在
      注意点：同进程多宿主共享静态 `ActivitySource`，其他测试类的请求也会经本测试的 processor 落入同一 store，故断言以"本次请求独有路径 → 访问日志 TraceId → span"为锚点，而非对集合基数做断言
- [x] 6.3 `TelemetryStoreTests`：`AddTrace` 超上限驱逐、计数有界
- [x] 6.4 Core 侧分位工具测试：空样本为 0、P99 ≥ P50、与既有 nearest-rank 结果一致
- [x] 6.5 `dotnet build MinGo.ReverseProxy.slnx` 0 error；DataPlane 114/114、Core 95/95 全绿
- [x] 6.6 手工核对：`OpenTelemetry:Enabled=false` 时应用仍可启动且内部遥测不丢
      实测：`--OpenTelemetry:Enabled=false` 启动，`/healthz/live` 返回 200，日志无 OTLP 相关记录、无未处理异常；
      内部路径（store 指标 + 访问日志 + 关联键回退）由 6.1 在完全没有 OTel 装配的环境下锁定

## 7. 收尾

- [x] 7.1 更新 `docs/基本可观测架构.md` 状态表中本阶段完成项
- [x] 7.2 在提案中明确"trace 与 trace_id 上送控制面尚未完成"，避免被误读为全链路已通
