# 路由驱动证书自动登记 — 实施任务

> 设计依据：`proposal.md` / `design.md`。沿用上期 `2026-11-27-cert-auto-enrollment` 的登记/同步管线，本变更只增"登记来源"与溯源能力。

## 1. Core：事件类型 / 纯策略 / 模型字段

- [x] 1.1 `Entities/GatewayEvent.cs`：`GatewayEventType` 追加 `RoutesChanged`（追加新成员，不影响既有值语义）
- [x] 1.2 `Models/RouteCertEnrollmentPolicy.cs`（新，纯函数）：
  - `CollectHosts(IEnumerable<RouteConfig>)`：通道过滤（`Enabled` 且 `IsNode || 空值`）、AnyHost（null/空/`*`）跳过、剥端口去尾点转小写（数据面 `NormalizeHost` 同口径）、`*.` 剥离记泛域、`IPAddress.TryParse`/`localhost`/无点域名跳过、按裸域合并（泛域取并集）
  - 返回 `IReadOnlyDictionary<string, bool>`（裸域 → IsWildcard）
- [x] 1.3 `Models/GatewayConfig.cs`：`CertificateConfig` 增加 `EnrollmentId (string?)`
- [x] 1.4 `Entities/ProxyConfigEntities.cs`：`ApiCertificateEntity` 增加 `EnrollmentId (string?)`

## 2. Infrastructure：迁移 / 批量删除

- [x] 2.1 EF 迁移：`ApiCertificates` 表加可空 `EnrollmentId` 列（存量行 null，向后兼容）
- [x] 2.2 `IApiDbService` + `ApiDbService`：`DeleteCertificatesByEnrollmentAsync(string enrollmentId)`（批量删除，供级联使用）
- [x] 2.3 `MapToCertificateModel/MapToCertificateEntity`：透传 `EnrollmentId`

## 3. Application：对账服务 / Worker / 发布点 / 溯源 / 作用域修复

- [x] 3.1 `Services/RouteCertEnrollmentReconcileService.cs`（新，Scoped）：
  - `ReconcileAsync(ct)`：设置守卫（`!Enabled || !Configured` 跳过）→ 读路由 → `CollectHosts` → 读登记建字典 → 无登记者 `RegisterAsync`（`EnrollmentException` 吞掉、其余记日志）→ `Enabled=false` 跳过 → 泛域升级（`IsWildcard=false` 且要求泛域 → 更新）
  - **无删除分支**（只增不删）
- [x] 3.2 `Workers/RouteCertEnrollmentReconcileWorker.cs`（新，IHostedService）：
  - 构造器仅 `IServiceProvider` + `ILogger`；`StartAsync` 订阅 `RoutesChanged` + 立即一次兜底对账；回调 = `Interlocked` 在途合并 + `CreateScope` 每事件独立作用域（照抄 `ConfigUpdateGrpcBroadcaster` 模式）；`StopAsync` 退订
- [x] 3.3 `ApiManagementService`：`CreateRouteAsync/UpdateRouteAsync/DeleteRouteAsync` 追加 `NotifyRoutesChangedAsync()`（发布 `RoutesChanged`，try/catch 吞错，不经 `NotifyGatewayConfigChangeAsync` 复用避免证书/集群变更误触发）
- [x] 3.4 `CertificateEnrollmentService.SyncAsync`：导入 `CertificateConfig` 回填 `EnrollmentId = enrollment.Id`
- [x] 3.5 `CertificateEnrollmentSyncWorker` **作用域修复**：构造注入改 `IServiceScopeFactory`，每轮对账 `CreateScope` 后解析 `CertificateEnrollmentService`（消除 captive dependency，Development `ValidateScopes` 下亦可启动）
- [x] 3.6 `ApiManagementService`：`DeleteCertificatesByEnrollmentAsync`（包装 `IApiDbService` 批量删除 + **单次** `NotifyGatewayConfigChangeAsync`）

## 4. ControlPlane.Api：注册与级联参数

- [x] 4.1 `Program.cs`：注册 `RouteCertEnrollmentReconcileService`（Scoped）、`RouteCertEnrollmentReconcileWorker`（AddHostedService）
- [x] 4.2 `CertEnrollmentsController.Delete`：`?deleteCertificates=true` 可选级联（先批量删证书、后移除登记；默认 false 保持既有语义）

## 5. 前端：徽标与级联交互

- [x] 5.1 `types/index.ts`：证书类型增加 `enrollmentId?: string | null`
- [x] 5.2 `pages/Certificates.tsx`：`enrollmentId` 非空 → 「自动接入」徽标
- [x] 5.3 `pages/CertEnrollments.tsx`：移除登记确认增加「同时删除已导入证书」选项（调 `DELETE ?deleteCertificates=true`）；页面加登记语义提示（路由自动登记 + 手动登记并存、只增不删、移除仅限此页）
- [x] 5.4 `api/client.ts`：remove 支持 `deleteCertificates` 参数

## 6. 测试与验证

- [x] 6.1 `RouteCertEnrollmentPolicyTests`（Core）：通道过滤（node 收/tunnel 弃/空值收）、禁用路由排除、AnyHost/IP/localhost/无点域名跳过、端口剥离、`*.` 映射与裸域合并、泛域并集
- [x] 6.2 `RouteCertEnrollmentReconcileServiceTests`（CP）：设置未启用/未配置 → 零调用跳过；新域名 → `RegisterAsync`（`IsWildcard` 正确、`UseStaging=false`）；已有登记 → 不重复登记；泛域升级；`Enabled=false` 登记保持原样；并发重复 → `EnrollmentException` 吞掉；**断言 `DeleteAsync` 从不被调用**
- [x] 6.3 `CertificateEnrollmentServiceTests` 补：`SyncAsync` 导入行携带 `EnrollmentId`；级联删除走批量方法且单次广播
- [x] 6.4 `dotnet build MinGo.ReverseProxy.slnx` 0 错误
- [x] 6.5 `dotnet test MinGo.ReverseProxy.slnx` 全绿（既有 476 + 新增，预期 Core 205+ / CP 145+）
- [x] 6.6 `cd frontend/min-go-console && pnpm build` 通过
- [x] 6.7 浏览器验证：保存 `node:*` 路由 → 证书接入页自动出现登记；保存/改域名/删除路由 → 登记跟随（增、不删）；隧道路由保存 → 不产生登记；停用 CertManager 设置 → 无 Failed 登记噪音；证书页面自动接入徽标；级联删除（含不级联对照）；Development 环境启动无 DI 异常

## 7. 文档（实施完成后）

- [x] 7.1 `AGENTS.md` 追加本变更段落（决策与验证状态，沿用既有条目格式）
- [x] 7.2 部署侧无新增配置；若发现 MinGo.Messaging 消费者存在同型作用域隐患（design D3 相邻风险），单独记录核查结论