## Why

阶段一让内存 `TelemetryStore` 这条内部路径的数字变正确了，但两条对外的标准管道仍然是断的，且没有任何编译错误或测试会提醒——它们静默地返回空数据。

**OTLP 指标出口是空壳。** `Program.cs` L121 的 `AddMeter("Gateway")` 订阅了一个名为 Gateway 的 Meter，但全仓库不存在任何 `new Meter` / `CreateCounter` / `CreateHistogram`（核查：0 命中）。中间件是手工把 `MetricPoint` 塞进队列，从未经过 OTel SDK。因此 aspire-dashboard / 任何 OTLP 后端看到的只有 AspNetCore 与 HttpClient 插针的框架指标，一个网关业务指标都没有。配置里写着 `OpenTelemetry.Enabled=true`，实际产出的网关观测量为零。

**代理内部链路不可见。** tracing 只 `AddSource("Gateway")`（L116），未订阅 YARP 自身的 `Yarp.ReverseProxy` ActivitySource。转发尝试、目的地选择、上游响应这些代理最该看到的 span 不进入链路。同时 `SerilogSetup.cs` L28 把 Yarp 命名空间抑制到 Warning，日志侧也拿不到细节。

**trace 采集侧完全没有落地。** `TelemetryStore.AddTrace`（L40）没有任何调用方——数据面自己那条 `Traces` 队列永远是空的，而控制面 `TelemetryController` L19-31 的 `GET /api/telemetry/traces` 读的是控制面进程内的另一个 store 实例。[docs/基本可观测架构.md](../../docs/基本可观测架构.md) 第六节设计的本地 `InMemoryExporter` 从未落地——这是整套自研观测方案里唯一缺失的关键环。结果是"最近请求列表"与"请求链路"之间没有桥：拿到一条 5xx 访问日志，无法跳到对应链路；拿到一条链路，无法回查日志。
（注意：补齐采集只是这座桥的第一段。trace 跨进程到控制面还需要 proto 字段，见 Impact 末尾的移交说明。）

**分位数只存在于时序曲线，总览卡片仍是平均值。** `MonitoringService.GetRequestMetricsAsync` 已用 `GetPercentile`（nearest-rank，L188-196）算出 `RequestMetrics.P95/P99`，但作为看板首要指标的 `MetricsSummary` 只有 `AverageResponseTime`，且控制台前端（`Monitoring.tsx` / `Dashboard.tsx` / `types/index.ts`）检索不到任何 P95/P99 展示。平均值恰好掩盖网关最关心的尾延迟：少数慢请求被大量快请求稀释。

## What Changes

- **真实 instruments**：新增 `GatewayMetrics`，在 `Meter("Gateway")` 上创建 `gateway.requests.total` / `gateway.requests.errors` Counter 与 `gateway.request.duration` Histogram，标签沿用阶段一已验证的有界集合；中间件同时向 instruments 与 `TelemetryStore` 记录
- **订阅 YARP source**：tracing 增加 `AddSource("Yarp.ReverseProxy")`，代理内部 span 进入链路并挂在 Gateway span 之下
- **本地 trace 落地**：新增 `LocalTraceExporter : BaseExporter<Activity>` + `SimpleActivityExportProcessor`，把结束的 Activity 写入数据面自己的 `TelemetryStore.Traces`。这让采集侧第一次真的有数据；控制面 `/api/telemetry/traces` 仍返回空，因为那条队列属于控制面进程，补齐它需要 proto 与心跳协议改造（阶段三）
- **日志与链路关联**：`AccessLogEntry` 增加 `TraceId` / `SpanId`，取自当前 Activity；Activity 补充 `http.request.method`、`url.path`、`http.response.status_code`、`server.address` 等语义属性
- **总览分位数**：`MetricsSummary` 增加 P50 / P95 / P99，复用已有的 `GetPercentile`（与 `RequestMetrics` 同一口径），平均值保留以兼容既有看板
- **禁用开关语义明确**：`OpenTelemetry:Enabled=false` 时 instruments 成为 no-op，本地 store 采集与分位数计算不受影响

## Capabilities

### New Capabilities

- `request-tracing`: 请求链路的采集与关联——Activity 生命周期、YARP 内部 span、本地 trace 存储、访问日志到链路的关联键

### Modified Capabilities

- `request-telemetry`: 新增"网关指标必须通过 OTel instruments 导出"与"延迟分布必须提供分位数"两条要求；阶段一确立的记录完整性、标签有界、代理维度不变

## Impact

- **`src/MinGo.DataPlane/Telemetry/`**：新增 `GatewayMetrics.cs`（instruments 与指标名常量）、`GatewayOpenTelemetry.cs`（生产与测试共用的装配单点）、`LocalTraceExporter.cs`；`GatewayTelemetryMiddleware.cs` 接入 instrument 记录、Activity 语义属性与访问日志关联键
- **`src/MinGo.DataPlane/Program.cs`**：OTel 注册收敛为 `AddOpenTelemetry().AddGatewayTelemetry(serviceName, endpoint)`
- **`src/MinGo.Core/Services/TelemetryStore.cs`**：`AccessLogEntry` 增加 `TraceId` / `SpanId`（内存模型，不动 gRPC proto）；`AddTrace` 改记账计数裁剪；`MaxTraces` 公开供测试引用
- **`src/MinGo.Core/Services/MetricPercentiles.cs`（新增）+ `Models/Metrics.cs`**：nearest-rank 工具集中到 Core，`MetricsSummary` 增加分位数字段
- **`src/MinGo.Application/Services/MonitoringService.cs`**：总览填充分位数，删除私有 `GetPercentile`，与时序共用同一实现
- **测试**：`GatewayTracingTests`（新增，真实 OTel 装配的 E2E）、`GatewayTelemetryMiddlewareTests`（关联键与名称契约）、`TelemetryStoreTests`（trace 上限）、`MetricPercentilesTests`（新增）；`InMemoryConfigurationSource` 从 E2E 私有嵌套类提为共享测试桩
- **依赖**：不新增 NuGet 包，`OpenTelemetry` SDK（含 `BaseExporter`、`Meter`、`Instrument`）已由 MinGo.DataPlane 引用
- **不在本变更范围（移交阶段三）**：trace 与访问日志的 gRPC 字段扩展（含 `TraceId`）、心跳上报的增量/聚合语义、真实 CPU/内存与健康状态采集、`/metrics` 出口、控制面 trace 读取侧（`GetTagValue` 走 `Activity.Tags`，读不到 int/double 形态的标签）。理由见 design.md Non-goals——那些改动需要同时动 proto 与上报协议，与本变更的采集层解耦才可控

## 落地结果（2026-11-12）

验证：`dotnet build MinGo.ReverseProxy.slnx` 0 error；MinGo.DataPlane.Tests 114/114、MinGo.Core.Tests 95/95 全绿。
`OpenTelemetry:Enabled=false` 启动实测：`/healthz/live` 200，日志无 OTLP 记录、无未处理异常，内部路径不依赖 OTel 装配。

与原计划的三处偏差，都已回写进 design.md：

1. **D4 的 `WithTracing((sp, builder) => ...)` 重载在 OpenTelemetry 1.15.3 不存在**（编译期 CS1593；从包内 `OpenTelemetry.xml` 枚举确认该版本只有无参与 `Action<TracerProviderBuilder>` 两种签名）。改用同一版本提供的 `AddProcessor(Func<IServiceProvider, BaseProcessor<Activity>>)` 解析 `TelemetryStore`，效果等价且不需要新包。
2. **`GET /api/telemetry/traces` 在本阶段后仍返回空**，与本提案初稿的说法相反。该端点读的是控制面进程内的 `TelemetryStore`，而 trace 只落在数据面自己的队列里；proto 与心跳不携带 trace。本阶段交付的是"数据面采集侧第一次有真实 trace"，跨进程可见属于阶段三。
3. **E2E 断言方式改变**：同进程内多个测试宿主共享静态 `ActivitySource`，别的测试类的请求也会经本测试的 processor 落入同一 store。因此不断言 `Traces` 的基数，而是用"本次请求独有路径 → 访问日志 `TraceId` → 对应 span"的锚点定位——这条跳转链本身正是本阶段要交付的能力。

移交阶段三时必须一并处理的两个下游缺口：控制面读取 span 标签走 `Activity.Tags`，而 .NET 10 下该投影不暴露 int/double 形态的标签值（`status`、`duration` 会读成 null，需改用 `EnumerateTagObjects`）；访问日志的 `TraceId` / `SpanId` 目前只存在于数据面内存模型，心跳 proto 里没有对应字段。
