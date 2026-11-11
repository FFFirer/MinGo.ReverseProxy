## 1. 中间件采集正确性

- [x] 1.1 重构 `GatewayTelemetryMiddleware.InvokeAsync`：把成功与异常路径收敛到单一记录出口（`Record` 方法，finally 调用）
- [x] 1.2 异常路径补记 `gateway.requests.total` 与 AccessLog（状态 500），使错误率分母完整
- [x] 1.3 `gateway.requests.errors` 判定收窄为 `status >= 500` 或管道异常，4xx 不再计入
- [x] 1.4 `error` 标签由 `ex.Message` 改为异常类型名；原始消息与堆栈交由 Serilog 输出

## 2. 标签基数与代理维度

- [x] 2.1 路由未命中时 route 取常量 `unmatched`，AccessLog 继续保留完整 Path
- [x] 2.2 在 `next()` 之后读取 `Yarp.ReverseProxy.Model.IReverseProxyFeature`，取 `Cluster?.Config?.ClusterId` 与 `ProxiedDestination?.DestinationId`
- [x] 2.3 特征缺失或目标未选出时写入哨兵 `none`，不写 null、不省略键
- [x] 2.4 核对热路径分配：total 与 duration 共用一份标签字典（每请求 1 次而非 3 次），`TelemetryStore` 裁剪判定改用记账计数而非 O(n) 的 `ConcurrentQueue.Count`

## 3. TelemetryStore 容量上限

- [x] 3.1 为每个指标名增加条数上限（默认 50000，`Telemetry:MaxMetricPoints` 可配置）与 FIFO 驱逐
- [x] 3.2 用 `ConcurrentDictionary` 计数与 `Interlocked` 维护点数，替代基于 `Count` 的判定
- [x] 3.3 驱逐累计数暴露为 `gateway.telemetry.dropped`（固定空标签，不带 route/cluster/status）
- [x] 3.4 确认时间裁剪（24h）与条数裁剪共存且互不破坏；自观测指标不递归记录驱逐

## 4. 测试

- [x] 4.1 新增 `tests/MinGo.DataPlane.Tests/GatewayTelemetryMiddlewareTests.cs`：异常请求同时进 total 与 errors 且有访问日志
- [x] 4.2 同文件：404 不计入 errors；N 请求 M 异常时 total=N、errors=M（无重复计数）
- [x] 4.3 同文件：未命中端点的多个随机路径只产生 `route=unmatched` 一个序列，访问日志仍含完整路径
- [x] 4.4 `ReverseProxyE2ETests` 遥测区新增断言：代理请求指标含 `cluster` 与 `destination` 真实值；502 场景带维度计入 errors
- [x] 4.5 `tests/MinGo.Core.Tests/TelemetryStoreTests.cs`：驱逐顺序、上限、dropped 计数、非递归、访问日志裁剪、DI 仍可解析无参构造
- [x] 4.6 `dotnet build MinGo.ReverseProxy.slnx` 0 error；DataPlane 104/104、Core 83/83 通过
- [x] 4.7 确认 `MonitoringService` / `TelemetryController` 无需改动即可得到非 null 的 ClusterId（`GetErrorMetricsAsync` 读 `cluster` 标签，现由采集端写入）

## 5. 收尾

- [x] 5.1 在变更说明中明示报表数值变化（total 上升、errors 下降、错误率不再可能 >100%），提示看板基线与阈值需重新校准（见 design.md D6 与本文件末尾"落地结果"）
- [x] 5.2 `docs/基本可观测架构.md` 中尚未落地的环节标注归属阶段

## 落地结果

行为变更（报表数值会随之变化，属预期修复）：

- `gateway.requests.total` 计数上升：异常请求此前只进分子不进分母
- `gateway.requests.errors` 计数下降：4xx 不再计入网关错误
- 错误率不再可能超过 100%；异常请求在访问日志中重新可见
- `cluster` / `destination` 标签首次有值，`MonitoringService.GetErrorMetricsAsync` 的 ClusterId 不再恒为 null
- 随机路径探测不再造成指标序列无界增长；超出容量上限时通过 `gateway.telemetry.dropped` 可见
