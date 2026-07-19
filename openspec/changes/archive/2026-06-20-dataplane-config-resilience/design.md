## Context

MinGo.ReverseProxy 是控制面+数据面分离的反向代理系统。DataPlane 通过 gRPC 双向流从 ControlPlane 接收 ConfigSnapshot（routes/clusters/certificates），应用到 YARP 的 `IProxyConfigProvider`。

**当前问题**：
- `Program.cs` 中 `WaitForInitialConfigAsync(60s)` 阻塞启动，配置到达前进程不服务
- 如果 ConfigSnapshot 包含无效配置（空 RouteId、ClusterId 引用不存在的 Cluster、非法 Address 等），YARP 内部 `ConfigValidator` 在 `MapReverseProxy()` 的 `InitialLoadAsync()` 中抛出 `InvalidOperationException`/`AggregateException`，导致进程崩溃
- DataPlane 没有区分"配置加载中"和"配置加载失败"两种状态

**YARP 2.3.0 行为**（经源码确认）：
- `MapReverseProxy()` 调用 `ProxyConfigManager.InitialLoadAsync().GetAwaiter().GetResult()` — 启动时阻塞验证
- 验证调用所有 `IProxyConfigProvider.GetConfig()`，合并 routes/clusters，然后运行 `ConfigValidator`
- 空配置（零 routes、零 clusters）是合法的，不会触发验证异常
- 验证失败抛 `AggregateException("The proxy config is invalid.", errors)`

## Goals / Non-Goals

**Goals:**
- DataPlane 启动后立即开始服务，不阻塞等待配置
- ConfigSnapshot 预检：校验失败时丢弃 snapshot、保持旧配置、记录 Warning 日志
- 通过 `/healthz/ready` 暴露配置加载状态
- 保持 `.LoadFromConfig(builder.Configuration)` 不变

**Non-Goals:**
- 不修改 ControlPlane 的配置推送逻辑（`config-validation-before-push` change 负责）
- 不修改 YARP 内部验证逻辑
- 不实现配置回滚（旧配置仅保留到下一次有效配置到达）
- 不修改 gRPC 重试退避机制

## Decisions

### 决策 1：移除 WaitForInitialConfigAsync，改为启动即服务

**选择**：删除 `await configSync.WaitForInitialConfigAsync(TimeSpan.FromSeconds(60))`，DataPlane 启动后立即开始服务。

**理由**：
- 空配置启动是 YARP 允许的合法状态，所有请求返回 503
- 配置异步到达后通过 YARP `IChangeToken` 热加载，无需重启
- 负载均衡器通过 `/healthz/ready` 判断节点是否可用，自动摘除未就绪节点

**替代方案**：
- 保留阻塞但缩短超时：仍然阻塞启动，不解决根本问题
- 保留阻塞并加 fallback 配置：需要维护本地缓存，增加复杂度

### 决策 2：在 DataPlaneConfigProvider.ApplyConfig() 中预检

**选择**：在 `ApplyConfig(snapshot)` 方法开头加入 `ValidateSnapshot(snapshot)` 调用，校验失败时 `return` 不替换 `_config`。

**理由**：
- 拦截点在 YARP 之前，防止无效配置进入 `IProxyConfigProvider`
- 保持旧配置不崩溃，符合"丢弃坏配置、保持旧配置"的要求
- 校验逻辑与 YARP `ConfigValidator` 对齐，但不完全重复（只校验会导致 YARP 崩溃的致命错误）

**替代方案**：
- 在 `ConfigSyncService.ApplyConfigSnapshotAsync` 的 catch 中处理：已经做了，但那是兜底，不是预防
- 包装 `MapReverseProxy()` 的 try-catch：空配置不会触发异常，真正的问题是无效配置到达后的热加载

### 决策 3：校验规则仅覆盖致命错误

**选择**：只校验会导致 YARP 抛异常的致命错误，不校验警告级问题。

**致命错误（必须拦截）**：
- `RouteId` 为空 → YARP 抛 `ArgumentException("Missing Route Id.")`
- `ClusterId` 为空 → YARP 抛 `ArgumentException("Missing Cluster Id.")`
- `Route.ClusterId` 引用的 Cluster 不存在 → YARP 运行时 503（不崩溃但功能异常）
- `Destination.Address` 非法 URI → YARP 代理时抛异常
- `LoadBalancingPolicy` 不在允许列表 → YARP 抛异常
- 重复 `RouteId` → YARP 抛异常

**非致命（仅 Warning，不拦截）**：
- `TransformsJson` 格式错误 → 降级为无 transforms
- `Cluster` 无 Destinations → Cluster 可用但无后端
- `CertificateBytes` 为空 → 跳过该证书

**理由**：最小化拦截范围，避免过度拒绝有效配置。YARP 对警告级问题有自己的处理（日志 + 降级）。

### 决策 4：Health Check 端点设计

**选择**：新增 `/healthz/ready` 端点，检查 `_lastApplySucceeded` 标志。

**理由**：
- 与已有的 `/healthz/live`（存活检查）配合，形成 liveness + readiness 双端点
- `/healthz/live` 返回 200（进程存活），`/healthz/ready` 返回 200/503（配置就绪）
- 负载均衡器（Nginx、K8s、Compose）用 readiness 判断是否发流量

**替代方案**：
- 单一 `/healthz` 端点：无法区分进程存活和配置就绪
- 检查 `_initialConfigReceived`：初始为空配置也是有效状态，不应标记为 unhealthy

## Risks / Trade-offs

### [风险] 空配置启动期间所有请求返回 503
**缓解**：这是期望行为——明确告知"配置尚未就绪"。负载均衡器通过 readiness 摘除节点，用户无感知。配置到达后自动热加载。

### [风险] `.LoadFromConfig()` 和 gRPC 配置合并可能冲突
**缓解**：当前 `appsettings.json` 无 `ReverseProxy` 节（配置完全由 gRPC 推送），合并不会产生冲突。如果未来添加文件配置，需确保 RouteId/ClusterId 不冲突。

### [风险] 校验规则与 YARP ConfigValidator 不完全同步
**缓解**：校验规则仅覆盖 YARP 的致命错误子集。如果 YARP 新增验证规则导致遗漏，`ConfigSyncService` 的 catch 兜底仍会捕获异常，不会崩溃。

### [Trade-off] 校验逻辑需要维护
**接受**：校验规则是 YARP 验证的子集，变更频率低。如果 YARP 升级引入新验证，需同步更新校验规则。

### [Trade-off] 旧配置可能过时
**接受**：丢弃坏配置后保持旧配置，旧配置可能已过时（如 Route 引用的 Cluster 已删除）。这是最小代价——比崩溃好。下一次有效配置到达会覆盖。
