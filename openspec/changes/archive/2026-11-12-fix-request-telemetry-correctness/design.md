## Context

本变更只动一处采集逻辑（`GatewayTelemetryMiddleware`）与一处存储（`TelemetryStore`），目的是让已有的报表数字变正确，而不是重构遥测管道。所有关键前提已在真实 YARP 2.3.0 运行时上验证，验证方式与结论如下。

**验证一：route 标签在有界与无界之间取决于是否命中端点。** 在 E2E 宿主中同时发送命中代理路由与未命中的请求，读回 `TelemetryStore.Metrics["gateway.requests.total"]` 的标签：

```
route=r1|status=200              ← 命中 YARP 路由，值 = RouteId，有界
route=GET /scan/aaa|status=404   ← 未命中任何端点，值 = "METHOD path"，无界
route=GET /scan/bbb|status=404
```

`ctx.GetEndpoint()?.DisplayName`（中间件 L33-34）对 YARP 路由返回 RouteId，这一点是有利的；风险只在 fallback 分支。

**验证二：代理维度在中间件的 next 之后可读。** 在与真实中间件完全相同的观测点反射读取 `IReverseProxyFeature`：

```
=== /proxy/orders -> 502
route=r1 cluster=backend proxied=d1 available=[d1] all=[d1]
=== /scan/aaa -> 404
IReverseProxyFeature = <null>
```

因此 cluster/destination 标签不需要任何新增基础设施即可得到；但必须为"特征为 null"（未命中代理）与"`ProxiedDestination` 为 null"（所有目标不可用，返回 502）两种情况准备哨兵值。

**实施者注意（避免重复踩坑）：** 该接口在 `Yarp.ReverseProxy.Model` 命名空间，不是 `Yarp.ReverseProxy.Forwarder`；属性路径为 `Route?.Config?.RouteId`、`Cluster?.Config?.ClusterId`、`ProxiedDestination?.DestinationId`。不存在 `ProxiedEndpoint`、`Result` 属性，`RouteModel`/`ClusterModel` 上没有 `ClusterId` 直挂属性。以上均由编译与运行实测得出。

## Goals / Non-Goals

**Goals**

- 每个经过中间件的请求，在 `gateway.requests.total` 与 AccessLog 中恰好记录一次，异常路径也不例外
- `gateway.requests.errors` 只反映网关侧失败（5xx + 管道异常），不再被客户端 4xx 污染
- 所有指标标签取值集合有界，且随机路径探测无法造成指标序列无界增长
- 指标携带 cluster 与 destination 维度
- `TelemetryStore` 的指标存储有明确容量上界

**Non-Goals（按阶段移交）**

- 不引入 OpenTelemetry `Counter`/`Histogram` Instrument，不订阅 `Yarp.ReverseProxy` 的 ActivitySource/Meter，不接通 `TelemetryStore.AddTrace` 与 trace 查询链路 —— 这些改变采集抽象层与数据流方向，属于"统一 OTel 采集与追踪关联"阶段
- 不实现 `/metrics` Prometheus 出口（`GatewayConfig.MetricsEndpoint` 仍是未兑现配置），不改心跳上报协议与 `AccessLogEntry` 的 gRPC 字段 —— 属于"标准化出口与上报容量"阶段
- 不改系统指标（心跳中 `CpuUsage`/`MemoryUsage` 恒为 0、`IsHealthy` 恒为 true 的硬编码） —— 与请求可观测性正交，单独排期
- 不在阶段一优化热路径分配结构；本变更遵循"不新增分配"的约束，但不做环形缓冲重写

## Decisions

**D1：未命中路由的 route 归一为常量 `unmatched`，而不是保留路径。**
备选是"保留路径 + 靠存储上限兜底"。否决原因：上限兜底只是把无界变成"到顶后丢弃"，而合法序列会被探测流量挤出；归一为哨兵值从源头消除基数爆炸。排查所需的路径信息并未丢失——AccessLog 的 `Path` 字段保留完整路径与查询串。

**D2：`error` 标签取异常类型名，不取 `ex.Message`。**
类型名集合天然有界（代码可控），消息含 URL/ID/时间戳则无界，且可能外泄内部信息。原始消息与堆栈由 Serilog 请求日志与框架异常日志承担，指标不重复承载。
备选"完全去掉 error 标签"否决：失去区分故障种类的能力，收益不抵损失。

**D3：cluster / destination 在 `next()` 之后从 `IReverseProxyFeature` 读取，特征缺失时写 `none`。**
写在标签字典里而不是 null，因为下游 `MonitoringService.cs` L151-159 用 `Tags.TryGetValue` 分组，null 值会归入 `unknown` 且序列化行为不确定；固定哨兵值让分组稳定。
destination 取值用 `DestinationId`：按 `auto-destination-id` 规范其格式为 `{clusterId}-{sequence}`，集合规模等于集群目标数，基数有界。
不把 destination 加进 AccessLog —— 需要改 gRPC proto，移交出口阶段。

**D4：`errors` 只统计 5xx 与异常，4xx 不计入。**
`gateway.requests.total` 已带 status 标签，需要看客户端错误时按标签下钻即可，不新增指标名，避免在阶段一之前扩大指标面。

**D5：`TelemetryStore` 增加每指标条数上限，并用入队计数器而非 `ConcurrentQueue.Count` 做裁剪判定。**
关键约束：`ConcurrentQueue.Count` 是 O(n) 快照，若在每请求热路径上用它判断是否超限，会把基数问题换成 CPU 问题——现有代码（`TelemetryStore.cs` L61、L74、L86）已存在这个模式，本变更对 Metrics 路径必须避免重复。
实现：每个指标名维护一个 `long` 计数（`Interlocked` 增减），超出上限时从队首驱逐；驱逐总数通过 `gateway.telemetry.dropped` 指标自观测。
自观测指标使用固定标签集（不带 route/cluster/status），否则自身又引入基数。上限取值与依据见 R1。

**D6：本变更会改变既有报表数值，属于预期的正确性修复。**
total 上升（异常请求补记）、errors 下降（4xx 移出）、错误率随之下降且不再可能超过 100%。看板的基线与任何阈值告警需要重新校准，这必须在发布说明中明示，否则会被误读为"流量突增、故障率下降"。

## Risks

- **报表基线跳变**（D6）：数值变化是修复的目标而非副作用，但对使用者是行为变更。缓解：变更说明 + 数值前后对照。
- **`IReverseProxyFeature` 读取的时机耦合**：该特征由 YARP 转发器写入，若未来管道把中间件挪到 `UseRouting` 之前，route 与 feature 双双失效。缓解：E2E 断言锁定 cluster/destination 标签有值，位置错动会立刻红。
- **条数上限设得过小的取舍**：过小会让高流量下的短窗曲线丢点。缓解：上限按"每指标点数"而非全局设置，并把驱逐计数暴露出来，使"正在丢"变成可见状态而不是静默行为。
