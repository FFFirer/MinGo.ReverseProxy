## Context

阶段一修好了内存 `TelemetryStore` 这条内部路径的正确性。本阶段处理"标准 OTel 管道静默失效"与"日志到链路的关联缺失"，仍然只在数据面内部完成采集层的改造；任何需要动 gRPC proto 与上报协议的内容都留给阶段三。

**关键前提已在运行时实测，不是查文档得来的。** 用一个监听所有 source 的 `ActivityListener` 跑一次真实代理请求，得到：

```
LISTEN_CANDIDATE Gateway
LISTEN_CANDIDATE Yarp.ReverseProxy
STOPPED source=Yarp.ReverseProxy op=proxy.forwarder kind=Server
        tags=[proxy.route_id=r1,proxy.cluster_id=backend,proxy.destination_id=d1]
STOPPED source=Gateway op=gateway.request: r1 kind=Server
        tags=[route=r1,cluster=backend,destination=d1,status=200,duration=76.9224]
STOPPED source=Microsoft.AspNetCore op=Microsoft.AspNetCore.Hosting.HttpRequestIn kind=Server
```

三个结论直接决定设计：

1. YARP 的 ActivitySource 名字确实是 `Yarp.ReverseProxy`（写成 `YARP.ReverseProxy` 或 `Yarp` 都会静默失效，正是本阶段要修的那类 bug）。
2. YARP 已经在自己的 span 上给出 `proxy.route_id` / `proxy.cluster_id` / `proxy.destination_id`，目的地维度在链路侧是现成的，无需自造。
3. 当前 `AddSource("Gateway")` 只收自己那条 span，`proxy.forwarder` 被丢弃——订阅之后外部后端才能看到"网关耗时"与"上游转发"的拆分。

## Goals / Non-Goals

**Goals**

- `AddMeter("Gateway")` 开始真实产出：`gateway.requests.total` / `gateway.requests.errors` 为 Counter，`gateway.request.duration` 为 Histogram，OTLP 后端能看到网关指标
- `proxy.forwarder` 进入链路，与 Gateway span 形成父子关系
- Activity 结束时落到 `TelemetryStore.Traces`，`GET /api/telemetry/traces` 返回真实数据
- AccessLog 携带 `TraceId` / `SpanId`，日志与链路可双向跳转
- `MetricsSummary` 给出 P50 / P95 / P99

**Non-Goals（移交阶段三）**

- 不改 `Heartbeat.proto` 的 `AccessLogEntry` / `MetricPoint` 消息，因此 `TraceId` 与 trace 样本暂时只存在于数据面本地；控制面看板的跨实例链路跳转属于传输协议改造，与采集层解耦后才可控
- 不改"心跳每个指标只上报最后 10 个点"的欠采样协议，也不引入 OTel `MetricReader` 聚合桥接：那会同时改掉存储语义、上报口径与 `MonitoringService` 的聚合数学，属于阶段三
- 不采集真实 CPU / 内存 / 实例健康状态
- 不实现 `/metrics` Prometheus 出口

## Decisions

**D1： instruments 与 `TelemetryStore` 双写，而不是让 store 从 `MetricReader` 桥接。**
`MetricReader` 交付的是聚合后的 sum / histogram bucket，而控制面查询与 `MonitoringService` 的现有数学（对逐请求点求和、求平均、取分位）建立在"原始点"之上。桥接会连带改掉存储语义、心跳协议和聚合口径三件事。
取舍：一次请求两条记录路径，代价是 `Counter.Add` / `Histogram.Record` 各一次（百纳秒级）与一个标签盒数组；收益是标准出口立刻可用且内部路径零风险。
风险是两条路漂移（同一次请求两个出口数值不一致）。缓解：契约测试断言 instrument 标签集与 store 标签集来自同一个构造点，并断言 `Telemetry:Enabled=false` 时内部路径不受影响。

**D2： instruments 用静态 `Meter("Gateway")` + 静态字段，不做 DI 注册。**
`UseGatewayTelemetry` 在多个测试宿主中被直接使用，而这些宿主只注册了 `TelemetryStore`；若中间件构造函数新增必须解析的依赖，每个测试宿主都要跟着改。静态 instruments 不改变中间件签名，OTel instrument 本身即线程安全且无宿主状态。
副作用：同进程多宿主（测试并行）会重复创建同名 instrument，OTel 视为同一 instrument 的重复注册并静默接受（类型/单位一致）。可接受。

**D3： 本地 trace 用 `SimpleActivityExportProcessor` 同步入队。**
`TelemetryStore.Traces` 上限 2000 且看板要"最近请求"，实时性优先；exporter 只做一次入队。
**必须同时修的隐患**：`AddTrace` 现在用 `while (Traces.Count > MaxTraces)` 裁剪，而 `ConcurrentQueue.Count` 是 O(n)。阶段一已为 Metrics/AccessLogs 换成记账计数，`AddTrace` 因当时无调用方而未动；一旦本阶段把它接进每请求路径，就会在每请求做一次 2000 元素的 O(n) 遍历——把观测开销直接压在热路径上。因此 `AddTrace` 改用记账计数是本次改动的前置条件，而非顺带优化。

**D4： 本地 processor 的依赖通过 `AddProcessor(Func<IServiceProvider, BaseProcessor<Activity>>>)` 解析。**
`LocalTraceExporter` 需要 `TelemetryStore` 实例，而 `TelemetryStore` 由同一个容器注册；`WithTracing(Action<TracerProviderBuilder>)` 的 lambda 拿不到 `IServiceProvider`，若改为手动构造 TracerProvider 会引入容器外状态。
原计划用 `WithTracing((sp, builder) => ...)` 重载，**实测不可用**：OpenTelemetry 1.15.3 的 `OpenTelemetryBuilderSdkExtensions.WithTracing` 只有无参与 `Action<TracerProviderBuilder>` 两种签名（从 `OpenTelemetry.xml` 文档枚举确认），编译报 CS1593。同一版本在 `TracerProviderBuilderExtensions` 上确实提供 `AddProcessor(Func<IServiceProvider, BaseProcessor<Activity>>)`，用它解析容器即可，无需新增包、也不改 `Program.cs` 的注册形状。

**D5： `TraceId` / `SpanId` 取自当前 Activity，且仅在 Activity 存在时写入。**
`StartActivity` 在没有 listener 时返回 null（默认配置下 OTLP exporter 存在，因此有 listener）。为保持链路可关联，中间件在 Activity 为 null 时仍写入 `ctx.TraceIdentifier`——ASP.NET Core 每请求稳定且与 W3C traceparent 对齐，避免"本地开发无 OTLP 时访问日志没有关联键"。
该取舍需要在规范里写明：关联键在有 OTel listener 时为 W3C TraceId，无 listener 时退化为 `TraceIdentifier`，两者在 aspire-dashboard 与本地日志中都能唯一定位一次请求。

**D6： 总览分位数复用 `GetPercentile`，口径与 `RequestMetrics` 一致。**
不在阶段二引入直方图桶：桶聚合属于阶段三上报协议改造的一部分，届时分位数应由 OTel Histogram 的显式桶或 HDR 摘要给出，而不是对最多 50000 个原始点排序。nearest-rank 在窗口受限时是近似值——这一点写入规范，避免被当作精确分位使用。

## Risks

- **双写漂移（D1）**：数值不一致会同时损害可信度。缓解：单一构造点 + 契约测试；阶段三改为单点读取时删除 store 侧手工打点。
- **同步 processor 放大热路径开销（D3）**：以记账计数替代 `Count` 为前提条件，并新增基准断言（队列长度判定不随已存点数线性增长）。
- **`TraceIdentifier` 与 W3C TraceId 混用（D5）**：两者格式不同，跨系统查询时需注意。缓解：规范中明确各自适用条件。
