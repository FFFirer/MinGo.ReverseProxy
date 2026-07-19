## 1. DataPlaneConfigProvider 预检逻辑

- [x] 1.1 在 DataPlaneConfigProvider 中新增 `ValidateSnapshot(ConfigSnapshot snapshot)` 方法，返回 `(bool IsValid, List<string> Errors)`
- [x] 1.2 实现 Route 校验：RouteId 非空、ClusterId 非空
- [x] 1.3 实现 Route-Cluster 引用完整性校验：Route.ClusterId 必须引用 snapshot.Clusters 中存在的 Cluster
- [x] 1.4 实现 Destination 校验：Address 必须是合法 URI（`Uri.TryCreate` with `UriKind.Absolute`）
- [x] 1.5 实现 LoadBalancingPolicy 校验：必须在允许列表 `RoundRobin|LeastRequests|PowerOfTwoChoices|FirstAlphabetical|Random` 中，或为空（使用默认值）
- [x] 1.6 实现 RouteId 去重校验：同一 snapshot 中不允许重复 RouteId
- [x] 1.7 实现 ClusterId 非空校验
- [x] 1.8 修改 `ApplyConfig(ConfigSnapshot snapshot)` 方法：开头调用 `ValidateSnapshot`，校验失败时 LogWarning + return 不替换 `_config`
- [x] 1.9 添加版本单调递增检查：`snapshot.Version <= _currentVersion` 时丢弃（Debug 日志）

## 2. 健康检查端点

- [x] 2.1 在 DataPlaneConfigProvider 中新增 `_lastApplySucceeded` 标志（volatile bool），ApplyConfig 成功时设为 true
- [x] 2.2 在 Program.cs 中注册 `AddHealthChecks()` 服务
- [x] 2.3 在 Program.cs 中新增 `/healthz/ready` 端点，检查 `_lastApplySucceeded` 状态
- [x] 2.4 确保 `/healthz/live` 端点已存在（存活检查，无依赖）

## 3. 启动流程修改

- [x] 3.1 在 Program.cs 中移除 `await configSync.WaitForInitialConfigAsync(TimeSpan.FromSeconds(60))` 调用
- [x] 3.2 确保 `configSync.StartAsync()` 保持 fire & forget（不阻塞）
- [x] 3.3 确保 `MapReverseProxy()` 在 StartAsync 之后调用（空配置启动）
- [x] 3.4 验证空配置启动不触发 YARP 验证异常

## 4. 日志与可观测性

- [x] 4.1 ApplyConfig 校验失败时记录 Warning 级别日志，包含所有错误详情和 snapshot Version
- [x] 4.2 ApplyConfig 成功时记录 Information 级别日志，包含 Version、RouteCount、ClusterCount
- [x] 4.3 版本过期丢弃时记录 Debug 级别日志

## 5. 测试

- [x] 5.1 创建 DataPlaneConfigProvider 单元测试：有效配置通过校验并应用
- [x] 5.2 创建单元测试：空 RouteId 被拒绝，旧配置保持
- [x] 5.3 创建单元测试：ClusterId 引用不存在的 Cluster 被拒绝
- [x] 5.4 创建单元测试：非法 Destination Address 被拒绝
- [x] 5.5 创建单元测试：非法 LoadBalancingPolicy 被拒绝
- [x] 5.6 创建单元测试：重复 RouteId 被拒绝
- [x] 5.7 创建单元测试：旧版本 snapshot 被丢弃
- [x] 5.8 创建单元测试：空 ClusterId 被拒绝
