## Why

`GatewayTelemetryMiddleware` 是数据面唯一的请求级采集点，但实测发现它的四类输出与下游消费者的假设不一致，导致现有监控报表的数字不可信，并且存在一个可被外部流量放大的内存风险。

以下问题全部经过实测或代码核查确认，非推测：

**异常请求被漏记，错误率可超过 100%。** 中间件的 catch 分支（`GatewayTelemetryMiddleware.cs` L86-100）只写 `gateway.requests.errors`，既不写 `gateway.requests.total`，也不写 AccessLog。而错误率按 `errors / total` 计算（`MonitoringService.cs` L30-32、`TelemetryController.cs` L52-55）。结果：管道里抛出未捕获异常的请求，进分子不进分母；这批请求恰恰是最需要排查的，却在访问日志里完全查不到。

**4xx 被当成网关错误。** `statusCode >= 400` 即计入 `gateway.requests.errors`（L61-69）。客户端路径打错、爬虫扫描产生的 404 会被计入网关错误率，报表无法区分"网关坏了"和"客户端请求坏了"。

**route 标签基数无界，且存储无容量上限。** 未匹配任何端点时 route 回退为 `$"{Method} {Path}"`（L34）。探针实测确认：代理命中时标签为 `route=r1`（RouteId，有界），未命中时退化为 `route=GET /scan/aaa`、`route=GET /scan/bbb`——每个不同的探测路径都产生一条永久的新指标序列。而 `TelemetryStore.AddMetric` 只做 24 小时时间裁剪、没有条数上限（`TelemetryStore.cs` L54-65）。任何人用随机路径扫一遍网关，就能持续撑大数据面内存，并挤掉合法序列。

**cluster / destination 标签从未写入，下游却在读它。** 采集端只放 `route` 和 `status`，但 `MonitoringService.cs` L159 从标签里读 `cluster`——恒为 null。对反向代理而言最有价值的维度（哪个集群、哪个目标慢了或挂了）现在完全不可见。可行性已验证：在中间件 `next()` 之后读取 `IReverseProxyFeature`，实测可得到 `route=r1 cluster=backend proxied=d1 available=[d1]`；未命中代理时该特征为 null，需回退处理。

**异常消息被当作指标标签值。** `Tags = { "error", ex.Message }`（L95）既有基数风险（消息常含 URL、ID、时间戳），又把内部细节写进指标，且丢失异常类型与堆栈。

## What Changes

- **每请求恰好记录一次**：异常路径同步补齐 `gateway.requests.total` 与 AccessLog，错误率分母不再缺失
- **errors 语义收窄**：`gateway.requests.errors` 只统计 5xx 与管道异常；4xx 保留在 total 的 status 标签中，不再计入网关错误
- **标签基数有界**：未匹配代理路由时 route 归一为固定哨兵值 `unmatched`；完整路径与查询串继续保留在 AccessLog 中，不损失排查能力
- **补齐代理维度标签**：从 `IReverseProxyFeature` 读取 `cluster` 与 `destination` 写入指标标签；特征缺失时写入 `none` 哨兵值，不写 null
- **异常标签类型化**：`error` 标签由 `ex.Message` 改为异常类型名（如 `TaskCanceledException`），原始消息与堆栈交由 Serilog 记录
- **指标存储加上限**：`TelemetryStore` 每个指标名增加条数上限与 FIFO 驱逐，并暴露驱逐计数，使基数风险可观测、有界

## Capabilities

### New Capabilities

- `request-telemetry`: 数据面请求遥测的采集正确性契约——记录完整性、指标语义、标签基数边界、代理维度来源

### Modified Capabilities

无（`proxy-request-logging` 只约束 Serilog 请求日志，本变更不改动其行为）

## Impact

- **`src/MinGo.DataPlane/Telemetry/GatewayTelemetryMiddleware.cs`**：采集逻辑重写，新增 `IReverseProxyFeature` 读取（需引用 `Yarp.ReverseProxy.Model`，该包已由 MinGo.DataPlane 间接引用，不新增依赖）
- **`src/MinGo.Core/Services/TelemetryStore.cs`**：`AddMetric` 增加条数上限与驱逐计数
- **`src/MinGo.Application/Services/MonitoringService.cs`**：无需改动即可从 cluster 标签得到真实值；错误率语义随之变正确
- **测试**：新增 `GatewayTelemetryMiddlewareTests`（异常路径、4xx、未匹配路由的单元验证）；`ReverseProxyE2ETests` 遥测区增加 cluster/destination 标签断言
- **不在本变更范围**：OpenTelemetry Instrument 化、trace 管道接通、`/metrics` 出口、心跳上报协议与 AccessLog 字段扩展、系统指标真实化。前三项见后续阶段提案，理由见 design.md 的 Non-goals
