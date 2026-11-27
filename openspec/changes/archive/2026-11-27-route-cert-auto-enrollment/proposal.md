# 路由驱动证书自动登记（数据面域名自动接入）

## Why

上期「证书自动接入」打通了登记 → CertManager 申请/续期 → 事件同步 → 导入证书管理 → 下发数据面的闭环，但**登记动作仍依赖人工**：每新增一条路由，都要有人记得去「证书接入」页登记域名。路由配置是反代侧域名的权威来源——路由变了，所需证书集合就变了，登记本应跟随路由自动收敛。

另一个现状缺口：接入通道决定了证书责任方。数据面节点通道（`node:*`）由控制面下发的证书终结 TLS；Cloudflare 隧道通道由 Cloudflare 边缘终结 TLS、自有证书管理。因此**只有数据面通道路由声明的域名需要自动登记**，隧道路由的域名不应触发申请（避免无效 ACME 尝试与 LE 限流浪费）。

本变更把「路由保存」接到「证书登记」：保存路由后经内部事件总线异步对账，自动确保数据面域名都已登记。同时补齐证书来源可见性（自动接入标记 + 级联清理），并修复上期引入的一个作用域缺陷（对账 worker 的单例捕获 Scoped 服务）。

## What Changes

- **路由域名自动收集**：新增纯策略 `RouteCertEnrollmentPolicy`——从路由集合收集需要证书的域名。口径：仅 `Enabled` 且接入通道为 `node:*`（或空值，视同本地数据面）的路由；`*`/空 Host（AnyHost）跳过；IP 字面量与 `localhost` 跳过；host 带端口先剥端口（与数据面 `NormalizeHost` 同口径）；`*.example.com` 剥通配后按裸域 + `IsWildcard=true` 登记（与 CertManager 泛域语义对齐）；同裸域合并、泛域标记取并集。
- **事件总线触发（D3 采纳方案）**：`GatewayEventType` 新增 `RoutesChanged`；路由增/改/删成功后 `ApiManagementService` 发布该事件（仅入队，保存路径零延迟）。新增 `RouteCertEnrollmentReconcileWorker`（IHostedService）订阅事件 + 启动时一次性对账（存量路由补登记），回调**每事件独立 DI 作用域**（沿用 `ConfigUpdateGrpcBroadcaster` 模式）执行对账。
- **对账语义：只增不删**：对账确保"收集到的域名都有登记"——不存在则走既有 `RegisterAsync`（探测→有证直接同步/无证申请）；host 出现 `*.` 而登记非泛域时升级 `IsWildcard`。**绝不自动删除登记**：仅 UI 登记的域名（未被任何路由引用）完整保留，移除只走「证书接入」页人工删除。
- **守卫**：CertManager 连接设置未启用/未配置时对账直接跳过（避免批量生成 Failed 登记）；禁用状态的登记保持原样（尊重用户显式关闭）。
- **证书来源可见性（D6）**：`ApiCertificates` 新增 `EnrollmentId` 列，`SyncAsync` 导入时回填；证书管理页对自动接入证书显示徽标；移除登记支持可选级联删除其导入的证书（批量删除、单次广播）。
- **签发能力暂不抽象**：本变更继续使用 `ICertManagerClient`；`ICertificateIssuer` 抽象（CertManager 成为其中一种实现）明确列为后续独立变更。
- **缺陷修复**：`CertificateEnrollmentSyncWorker` 改为注入 `IServiceScopeFactory`、每轮对账独立作用域——消除单例持有 Scoped 服务（连带一个终身 `AppDbContext`）的 captive dependency。

## Capabilities

### New Capabilities

- `route-cert-auto-enrollment`: 路由域名收集口径（通道过滤/通配映射/非法域名过滤）、RoutesChanged 事件发布、对账 worker（订阅 + 启动兜底 + 独立作用域）、只增不删的对账语义、设置守卫与禁用尊重、泛域升级。

### Modified Capabilities

- `cert-auto-enrollment`: 登记导入证书回填 `EnrollmentId` 溯源；移除登记支持可选级联删除导入证书（批量删除单次广播）；对账 worker 作用域修复。

## Impact

- **MinGo.Core**：`GatewayEventType.RoutesChanged`；`RouteCertEnrollmentPolicy` 纯策略；`CertificateConfig`/`ApiCertificateEntity` 增加 `EnrollmentId`。
- **MinGo.Infrastructure**：`ApiCertificates` 表加列（1 个 EF 迁移）；`IApiDbService`/`ApiDbService` 新增按登记批量删除证书方法。
- **MinGo.Application**：`RouteCertEnrollmentReconcileService`（对账编排）；`RouteCertEnrollmentReconcileWorker`（订阅 + 启动兜底）；`ApiManagementService` 路由三方法发布 `RoutesChanged`；`CertificateEnrollmentService.SyncAsync` 回填 `EnrollmentId`；`CertificateEnrollmentSyncWorker` 作用域修复。
- **MinGo.ControlPlane.Api**：`Program.cs` 注册对账服务与 worker；`CertEnrollmentsController.DELETE` 增加可选级联参数。
- **frontend/min-go-console**：证书页「自动接入」徽标；证书接入页移除登记时可选级联删除 + 登记语义提示文案。
- **不涉及**：proto、数据面、Cloudflare 对账链路、MinGo.Messaging 接线——零改动。
- **兼容性**：新列可空、存量行默认 null（人工证书），迁移向后兼容；登记表零结构变更；对账只增不删，既有登记（含上期手动登记）行为不变。