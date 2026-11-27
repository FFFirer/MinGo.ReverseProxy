# 路由驱动证书自动登记 — 设计

## Context

上期「证书自动接入」（`openspec/changes/2026-11-27-cert-auto-enrollment/design.md`）已建立完整管线，本设计复用其全部结论，仅新增"登记来源"。关键现状：

- 登记编排 `CertificateEnrollmentService`：`RegisterAsync`（唯一性校验 → 探测 → 有证直接同步 / 404 才 POST 申请）、`SyncAsync`（下载 PFX → 解析 → (DomainName, Thumbprint) 幂等 → `IApiManagementService.CreateCertificateAsync` 导入并广播）、`HandleCompletedAsync/HandleFailedAsync`（事件消费者与对账 worker 共用 SyncAsync）。
- 接入通道语义（`ConnectionIds`，`src/MinGo.Core/Models/GatewayConfig.cs:9-24`）：`ConnectionId` 以 `node:` 前缀为数据面节点通道（证书由控制面下发、数据面终结 TLS）；其余为 Cloudflare 隧道（边缘终结 TLS、证书由 Cloudflare 管理）。路由模型默认 `node:default`，迁移已把存量 `local` 改写为 `node:default`。
- 内部事件总线 `IMessageNotificationService`（`MemoryMessageNotificationService`，单例）：内存队列 + 单线程消费循环（构造器 `Task.Run`，回调运行在**无 DI 作用域的裸线程**上），订阅者按 `GatewayEventType` 过滤。唯一订阅先例 `ConfigUpdateGrpcBroadcaster`：IHostedService + `IServiceProvider` + **每回调 `CreateScope()`**。
- 服务生命周期（`Program.cs`）：`IApiDbService`/`ICertificateEnrollmentStore`/`ICertManagerClient`/`CertificateEnrollmentService` 全部 Scoped；`MemoryMessageNotificationService` Singleton；`CertificateEnrollmentSyncWorker` 经 `AddHostedService` 注册为**单例**，但其构造器注入 Scoped 的 `CertificateEnrollmentService`——captive dependency 病灶（见 D3）。
- 证书下发语义（`cert-sync-reload` spec）：快照按路由域名交集过滤、同域名择优最晚到期、不过滤过期证书——自动接入证书与人工证书在下发链路同构，本变更不触碰。

## Goals / Non-Goals

**Goals**

1. 路由保存（增/改/删）后，数据面通道域名**自动**获得登记，人工无需再记。
2. 只处理数据面通道路由的域名（`node:*`）；隧道路由域名不触发登记（Cloudflare 自有证书管理）。
3. 对账**只增不删**：仅 UI 登记的域名完整保留；移除登记仅限人工，避免误删与预登记丢失。
4. 自动接入证书在证书管理页可见来源；移除登记可级联清理其导入证书。
5. 修复 `CertificateEnrollmentSyncWorker` 的 captive dependency。

**Non-Goals**

- 不做 `ICertificateIssuer` 抽象（继续用 `ICertManagerClient`，抽象列为后续独立变更）。
- 不自动删除/停用任何登记；不做内部域名智能识别（`foo.local` 类一次 Failed 噪音可接受）。
- 不把登记失败回传给路由保存响应（事件异步、失败落登记状态与日志）。
- 不改 proto、不改数据面、不改 Cloudflare 对账链路与 MinGo.Messaging 接线。
- 不实现设计文档遗留的 Pending 超时 status 检查（对账仍走 SyncAsync）。

## Decisions

### D1 域名收集口径：通道声明 + 启用路由 + 规整映射

收集函数 `RouteCertEnrollmentPolicy.CollectHosts(routes)`（纯函数，Core 层）：

1. **通道过滤**：`route.Enabled && (ConnectionIds.IsNode(route.ConnectionId) || string.IsNullOrEmpty(route.ConnectionId))`。空 ConnectionId 视同本地数据面（模型默认即 `node:default`，空值只可能出现在旧数据）；显式隧道等非 node 值全部跳过——按接入通道声明判定证书责任方。
2. **Host 规整**：`route.Match.Host` 为 null/空/`*`（AnyHost）→ 跳过；按数据面 `NormalizeHost` 同口径剥端口、去空白、去尾点、转小写；`*.` 前缀剥离并记 `isWildcard=true`。
3. **非法域名过滤**：`IPAddress.TryParse` 命中（IP 无公共 CA 语义）、`localhost`、单标签或无点域名 → 跳过（各记一条 Debug 日志）。
4. **合并**：按裸域去重；同裸域既有 `*.x` 又有具体 host 时泛域标记取并集（登记一个泛域名即可覆盖）。
5. 结果集 `IReadOnlyDictionary<string, bool>`（裸域 → 是否泛域），供对账比对。

保留接口形态：路由 `Match.Host` 是单值字符串（无多域名数组），本变更不做结构化扩展。

### D2 事件总线触发：RoutesChanged 信号 + 串行消费 + 在途合并

- `GatewayEventType` 追加 `RoutesChanged`（`src/MinGo.Core/Entities/GatewayEvent.cs`）。不复用 `ConfigUpdate`——后者也由证书导入/集群变更触发，会导致每张证书同步都触发一次无意义对账（噪音回路）。
- **发布点**：`ApiManagementService` 的 `CreateRouteAsync/UpdateRouteAsync/DeleteRouteAsync`，在既有 `NotifyGatewayConfigChangeAsync()` 之后追加 `NotifyRoutesChangedAsync()`（同样 try/catch 吞错）。`PublishEventAsync` 仅入队返回，路由保存路径零延迟。每条保存产生两个事件：`ConfigUpdate`（gRPC 快照广播，既有）与 `RoutesChanged`（证书登记对账，新增）。
- **事件无负载**：对账以"当前路由全量"为准，事件只作信号——重复、乱序、丢失均无状态问题（收敛路径：启动兜底 + 下次路由保存 + 每小时证书对账 worker）。
- **串行消费链的延迟**：消费循环 `Task.WhenAll` 等所有回调完成才处理下一条。对账本身是纯读 + 网络调用仅发生在**新域名**（`RegisterAsync` 探测/申请，登记已存在即查表跳过），实践中每次保存 0-1 个新域名、1 次探测（404/200 快速返回），对队列中后续 `ConfigUpdate` 广播的延迟影响可忽略。批量导入路由的极端场景留作后续（Channel 解耦），本期不做。
- **在途合并**：worker 持 `int _reconciling`，`Interlocked.CompareExchange` 抢占；已在对账则跳过本次事件——无状态损失（对账读最新路由，且被跳过后总有下次触发或每小时收敛）。

### D3 消费者形状与作用域纪律

新增 `RouteCertEnrollmentReconcileWorker : IHostedService`（Application 层，注册于 `AddHostedService`），照抄 `ConfigUpdateGrpcBroadcaster` 模式：

- 构造器**只注入** `IServiceProvider` + `ILogger`（任何 Scoped 依赖构造注入都会重蹈 worker 病灶）。
- `StartAsync`：`CreateScope` 解析 `IMessageNotificationService` → `SubscribeAsync("RouteCertEnrollmentReconcileWorker", [RoutesChanged], callback)` → **随后立即执行一次兜底对账**（存量路由补登记；若启动期间遗漏事件亦有此兜底）。启动顺序安全：`EnsureDefaultNodeAsync`/迁移在 `app.Run()` 前已完成，订阅与兜底在宿主启动阶段。
- 回调：`Interlocked` 在途检查 → `using var scope = _serviceProvider.CreateScope()` → 解析 `RouteCertEnrollmentReconcileService` → `ReconcileAsync`。**每事件一个作用域、DbContext 随作用域即弃**，不跨事件共享，不捕获 Scoped 实例进闭包。
- `StopAsync`：退订（与广播器同构）。

**顺带修复 `CertificateEnrollmentSyncWorker`**：改构造注入 `IServiceScopeFactory`，每轮对账 `CreateScope` 后再解析 `CertificateEnrollmentService`。现状病灶分析：

- **Development 环境**（`ValidateScopes=true` 默认）启动即抛 `InvalidOperationException`（单例消费 Scoped）——本机一直以 Production 运行而被掩盖；
- **Production**（`ValidateScopes=false`）静默成立 captive dependency：worker 终身持有同一 `AppDbContext`——变更追踪器无界增长（每轮查询/更新实体全部缓存）、身份映射陈旧读（请求侧对登记状态的更新对 worker 可能不可见）、长命上下文与请求侧 SQLite 写操作竞争。

**相邻风险**（本期不动，联调核查项）：`CertificateCompletedEventConsumer` 等 MinGo.Messaging 消费者同样依赖 Scoped 服务，其解析方式由第三方包决定，若从根容器解析则存在同型隐患。

### D4 对账语义：只增不删

`RouteCertEnrollmentReconcileService.ReconcileAsync(ct)`：

1. 读 `ICertManagerSettingsService.GetAsync()`：`!Enabled || !Configured` → 记日志直接返回（守卫，防止对每个新域名生成 Failed 登记）。
2. 读全部路由 → `RouteCertEnrollmentPolicy.CollectHosts`。
3. 读全部登记 → 按归一化域名建字典。
4. 逐域名：
   - 无登记 → `CertificateEnrollmentService.RegisterAsync(new RegisterCertificateEnrollmentRequest { Domain, IsWildcard, UseStaging = false })`。捕获 `EnrollmentException`（并发重复）吞掉；其余异常记日志不中断（`RegisterAsync` 内部已把客户端错误落 Failed 状态）。
   - 有登记且 `Enabled=false` → 跳过（尊重显式关闭，不重新启用、不重复登记）。
   - 有登记且 `IsWildcard=false` 但收集结果要求泛域 → 更新登记 `IsWildcard=true`（下次同步导入双行覆盖子域）。反向（泛→具体）不降级——泛域证书本就覆盖裸域。
5. **无删除分支**。仅 UI 登记的域名（未被任何路由引用）完整保留；域名从路由移除后登记仍保留（CertManager 继续续期），清理唯一路径是「证书接入」页人工删除（可选级联，见 D6）。

**代价与对策**：已无路由引用的登记继续续期，会在证书库积累新证书行。快照按路由域名交集过滤，这些行不会被下发（无任何流量影响）；行数增长由 D6 级联清理与人工移除解决；库内清理策略（保留最近 N 张）若需要再列为后续优化。

### D5 设置守卫与禁用尊重

- 守卫放在对账入口（D4 第 1 步）：`Enabled=false` 或未配置 → 什么都不做。这替代了取消的「本地证书覆盖判定」——D2 采纳方案 B（不因已有本地证书而跳过登记），唯一的前置条件就是连接可用。
- `Enabled=false` 的登记：路由仍引用 → 保持原样；对账不重新启用。此状态自愈路径：用户手动启用或同步（事件/每小时 worker 均尊重 `Enabled`）。
- Staging 开关只有人工 UI 能设置，对账创建的登记恒为 `UseStaging=false`。

### D6 证书溯源与级联清理

- `ApiCertificateEntity`/`CertificateConfig` 增加 `EnrollmentId (string?)`；`SyncAsync` 导入时回填 `enrollment.Id`；映射 `MapToCertificateModel/Entity` 透传。存量行与人工上传恒 null。证书管理页对非空行显示「自动接入」徽标。
- **级联清理**：`DELETE /api/cert-enrollments/{id}?deleteCertificates=true`（默认 false，保持"仅移除登记"的既有语义）。实现要点：登记删除前先经 `IApiDbService.DeleteCertificatesByEnrollmentAsync(enrollmentId)` 批量删除 + 单次 `NotifyGatewayConfigChangeAsync`（**不得**逐张 `DeleteCertificateAsync`，那会触发 N 次广播）。
- 对账不删除登记（D4），故对账路径永不级联；级联只发生在人工移除——用户显式拥有证书行处置权。

### D7 签发能力抽象：明确延迟

`ICertificateIssuer` 抽象（CertManager 成为其中一种实现）不在本期：对账与登记服务继续依赖 `ICertManagerClient`。抽象将来落点的接缝已清晰——`CertificateEnrollmentService` 只从客户端要"字节（PFX）"，解析/幂等/导入/广播全在控制面，"任何签发方产物都入证书管理"的约束天然成立；届时 `ExternalCertificateId` 由 `Guid?` 泛化为不透明字符串、设置服务按签发方类型扩展。本期不为此做任何预埋结构。

## 时序

**路由保存 → 自动登记（事件路径）**

```
用户 → POST/PUT/DELETE /api/routes/{id}
  → ApiManagementService 写库（成功）
  → NotifyGatewayConfigChangeAsync()  → ConfigUpdate 事件（gRPC 快照广播，既有）
  → NotifyRoutesChangedAsync()        → RoutesChanged 事件入队（立即返回）
MemoryMessageNotificationService 消费循环（裸线程，无作用域）
  → RouteCertEnrollmentReconcileWorker 回调
      ├─ Interlocked 在途检查（已在跑 → 跳过）
      └─ CreateScope() → RouteCertEnrollmentReconcileService.ReconcileAsync
           1. 设置未启用/未配置 → 跳过
           2. CollectHosts(全部启用 node 通道路由)
           3. 无登记的域名 → RegisterAsync（探测→同步 / 404→申请→Pending）
           4. 泛域升级（IsWildcard 漂移）
           （不删除任何登记）
```

**启动兜底**

```
app.Run → RouteCertEnrollmentReconcileWorker.StartAsync
  → 订阅 RoutesChanged
  → 立即一次 ReconcileAsync（存量 routes 补登记；事件丢失窗口收敛）
```

**级联清理**

```
用户 → DELETE /api/cert-enrollments/{id}?deleteCertificates=true
  → DeleteCertificatesByEnrollmentAsync(id)（批量删除 + 单次 ConfigUpdate 广播）
  → 删除登记记录
```

## Risks / Trade-offs

| 风险 | 对策 |
| --- | --- |
| **LE 限流**：首次部署兜底对账会对所有存量数据面域名集中申请 | 登记前探测（有证直接同步不申请，`RegisterAsync` 既有行为）；每个域名仅此一次申请；限流后果为登记转 Failed，UI 可见、手动重试，不阻塞路由保存。 |
| **内部域名噪音**：`foo.local` 等无法从字面识别，会经一次探测+申请后落 Failed | 一次性噪音（有登记后对账不再重复申请）；IP/`localhost`/无点域名已在收集层过滤；Failed 状态 UI 可见。后续如需可加排除列表（本期不做）。 |
| **串行消费延迟**：对账回调阻塞消费队列 | 网络调用仅发生在无登记的新域名（每次保存 0-1 次探测）；在途合并防堆积；极端批量场景后续用 Channel 解耦。 |
| **登记不删除的续期成本**：域名移除路由后登记继续续期、证书行积累 | 快照按路由域名过滤不下发，无流量影响；人工移除 + D6 级联清理即回收；库内保留策略列为后续可选优化。 |
| **作用域修复的回归面**：`CertificateEnrollmentSyncWorker` 改为独立作用域 | 修复消除 Development 启动异常与陈旧读；行为面不变（每轮对账仍遍历 SyncAsync）；全量单测 + 联调验证。 |
| **并发登记竞态**：两次保存并发对账同一新域名 | 域名唯一索引兜底；`EnrollmentException` 吞掉视为已存在；在途合并串行化绝大部分场景。 |
| **事件丢失**：内存总线无持久化，进程崩溃丢 RoutesChanged | 启动兜底 + 下次保存 + 每小时证书对账三条收敛路径；登记缺失的最坏后果是域名未自动登记（无既有功能的回归）。 |
| **开发环境启动**：修复后 worker 在 Development 可正常解析 | `ValidateScopes=true` 下独立作用域模式通过校验；与广播器先例一致。 |