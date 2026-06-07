## 1. proto 定义扩展

- [x] 1.1 在 `dataplane.proto` 的 `EventType` 枚举中追加 `CONFIG_QUERY = 7` 和 `CONFIG_REPORT = 8`
- [x] 1.2 运行 `dotnet build` 验证 protobuf 代码生成无报错

## 2. 数据面：CONFIG_QUERY 事件处理

- [x] 2.1 在 `DataPlaneConfigProvider` 中添加 `GetConfigSnapshotJson()` 方法，将当前 `IProxyConfig` 序列化为含 `version`、`routes`、`clusters` 的 JSON 字符串
- [x] 2.2 创建 `ConfigQueryHandler`，通过 EventSubscription 双向流监听事件，处理 `CONFIG_QUERY` 事件
- [x] 2.3 事件处理器调用 `DataPlaneConfigProvider.GetConfig()` 获取运行时配置，构造 `CONFIG_REPORT` 事件消息并写入 EventSubscription 上行流
- [x] 2.4 处理异常：`GetConfig()` 失败时记录日志但不崩溃

## 3. 控制面：InstanceConfigQueryService

- [x] 3.1 新建 `Services/InstanceConfigQueryService.cs`，实现查询-等待-匹配逻辑：
  - `ConcurrentDictionary<string, TaskCompletionSource<ConfigQueryResult>>` 存储待完成的查询
  - `QueryConfigAsync(instanceId)` 通过 TrySendEventAsync 回调发送 CONFIG_QUERY 并等待响应
  - `HandleConfigReport(eventId, dataJson)` 由 EventSubscriptionService 调用，匹配并完成 TCS
  - 10 秒超时：`await Task.WhenAny(tcs.Task, Task.Delay(10000))`
- [x] 3.2 注册为 Singleton 服务到 DI 容器
- [x] 3.3 在 `EventSubscriptionService` 中注入 `InstanceConfigQueryService`，收到 `CONFIG_REPORT` 时调用 `HandleConfigReport()`

## 4. 控制面：InstancesController config 端点

- [x] 4.1 在 `InstancesController` 中新增 `GET /api/instances/{id}/config` 端点
- [x] 4.2 端点逻辑：
  - 调用 `IGatewayInstanceService.GetInstanceAsync(id)` 检查实例是否存在
  - 通过 `EventSubscriptionService.IsConnected()` 检查实例是否在线
  - 注入 `InstanceConfigQueryService` 发起查询
  - 成功 → 返回 200 + config JSON
  - 实例离线 → 返回 503
  - 不存在 → 返回 404
  - 超时 → 返回 504
- [x] 4.3 添加错误处理和日志

## 5. 前端：实例配置展示

- [x] 5.1 在 `types/index.ts` 中添加配置查询响应的 TypeScript 类型定义（`InstanceConfigResponse`、`InstanceRouteInfo`、`InstanceClusterInfo` 等）
- [x] 5.2 在 `Instances.tsx` 操作列的"删除"按钮前添加"查看"按钮，使用 `.btn-text-primary` 样式
- [x] 5.3 创建 `InstanceConfigModal` 组件：
  - Modal 标题：`实例配置 - {instanceName}`
  - Header 区域显示 config version 和实例地址
  - Routes 面板：表格展示（路由ID, 目标集群, 匹配路径, 匹配域名）
  - Clusters 面板：卡片列表，每张卡片展示集群名、负载均衡策略（中文映射）、目标地址列表（含健康状态 badge）
  - 空状态处理：无路由时显示"暂无路由规则"，无集群时显示"暂无目标集群"
- [x] 5.4 Modal 中增加"刷新"按钮，点击重新请求配置并更新展示
- [x] 5.5 处理加载状态（按钮 disabled）和错误状态（显示错误信息，如实例离线或不存在的提示）
- [x] 5.6 实现 Modal 关闭（X 按钮 + 点击外部关闭）

## 6. 集成验证

- [x] 6.1 启动 ControlPlane.Api + 一个 DataPlane 实例
- [x] 6.2 创建测试路由/集群，推送配置到数据面
- [x] 6.3 通过前端或直接调用 `GET /api/instances/{id}/config` 验证返回的配置与推送的一致
- [x] 6.4 断开数据面连接，验证离线状态返回 503
- [x] 6.5 验证不存在的实例 ID 返回 404
