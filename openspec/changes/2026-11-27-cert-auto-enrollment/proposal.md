# 证书自动接入（对接 MinGo.CertManager）

## Why

当前反代证书完全依赖人工：在 MinGo.CertManager（或其它工具）里申请、手动导出 PFX、再到控制面上传（`CertificatesController` 仅支持上传/解析/删除）。证书到期只体现在列表 `IsValid` 标红与数据面日志告警，没有任何自动续期路径。域名数量增长后，人工换证成为周期性运维负担，漏换即造成站点断链。

MinGo.CertManager 已经具备完整的 ACME 生命周期能力，但从未与控制面联动：

- **申请与续期**：Let's Encrypt + 阿里云 DNS-01；Quartz 每 24h 扫描、到期前 30 天自动续期（续期新建记录、经同一签发管线）。
- **远程接入面**：`/api/external` 三接口（申请、状态、按域名下载 PFX/PEM/CRT），`Authorization: ApiKey <key>` 认证。
- **事件面**：MinGo.Messaging（SimpleMessageBroker transport）发布 `CertificateCompletedEvent` / `CertificateFailedEvent`（申请与续期走同一管线，均会发布）。

三块能力拼起来，正好构成「登记域名 → 自动申请 → 事件驱动同步 → 入库 → 下发数据面 → 自动续期再同步」的全托管闭环。本变更在控制面实现这个闭环，使证书在反代侧**零人工**流转。

## What Changes

- **CertManager 远程连接**：新增 `CertManagerSettings` 单行配置（BaseUrl、ApiKey、Enabled），ApiKey 用现有 DataProtection 加密落库（与 CloudflareSettings 同模式）；提供连通性测试（ApiKey 错误与网络不通可区分）。
- **证书登记资产**：新增 `CertificateEnrollments` 表——域名唯一，含泛域名/Staging 开关、外部证书 ID、同步状态、最近导入指纹、最近错误。登记即接入：先按域名探测下载，CertManager 已有有效证书则直接同步；没有才发起申请。
- **事件驱动同步**：控制面引入 MinGo.Messaging（nuget.org 公开包，与 CertManager 对齐 0.0.4），订阅 `certmanager.certificate.completed` / `failed` 两个事件；事件仅作**信号**——收到 Completed 即按域名从 `/api/external/certificates/{domain}/download` 拉取 PFX（随机密码），指纹比对幂等导入 `ApiCertificates`，触发现有 ConfigUpdate 广播（复用 `cert-sync-reload` 链路）下发到数据面。泛域名证书导入两条行（裸域 + `*.domain`），使现有 `CertificateSnapshotPolicy` 的通配覆盖语义直接可用。
- **对账兜底**：后台工作器定时对账（状态查询 + 下载比对指纹），补偿 SimpleMessageBroker「拉取即消费、至多一次」的丢事件窗口；同时消化事件重复（CertManager 命令路径存在双发布）。
- **续期零成本**：CertManager 续期签发后发布同一 Completed 事件，按域名下载接口返回最新有效证书，控制面同步路径天然得到新证。
- **前端「证书接入」页**：连接设置卡片（BaseUrl/ApiKey/测试连接/启用）+ 登记列表（域名/类型/状态/到期/最近同步/错误），支持登记、手动同步、移除登记。
- **发布/接收接线（broker 复用环境现网）**：SimpleMessageBroker 已在环境运行（`http://100.102.18.111:5292`，实测 200，与 CertManager/控制面同处 Tailscale 网络），**不新增任何部署**。发布侧——CertManager 开启 `Messaging:Enabled`、publisher `CertBus` 指向该 broker（事件发布点已存在，启用即生效，零代码改动）；接收侧——控制面 appsettings 增加同构 `Messaging` 节并订阅 `completed`/`failed` 两个事件。

## Capabilities

### New Capabilities

- `cert-auto-enrollment`: 证书登记资产 CRUD、远程申请、事件订阅与 PFX 拉取导入、指纹幂等、对账工作器、泛域名双行导入、连接设置与连通测试、前端管理页。

### Modified Capabilities

- `cert-sync-reload`: 自动导入路径同样触发 ConfigUpdate 广播；快照组装/择优/下发规则不变（自动导入与人工上传在 `ApiCertificates` 中同构）。

## Impact

- **MinGo.Core**：`CertManagerSettingsEntity`、`CertificateEnrollmentEntity` 与对应 DTO/状态枚举；`ConnectionIds` 等无关模块零改动。
- **MinGo.Infrastructure**：`AppDbContext` 注册两张新表；1 个 EF 迁移；`IApiDbService` 扩展登记/设置读写；新增 `CertManagerClient`（IHttpClientFactory，命名 "CertManager"，复用现有 AddNamedHttpClients 模式）。
- **MinGo.Application**：`CertificateEnrollmentService`（登记/同步/对账编排，复用证书导入与指纹去重逻辑）；`CertificateEnrollmentSyncWorker`（BackgroundService 对账循环）；Messaging 消费者（Completed/Failed → 触发同步/记录失败）。
- **MinGo.ControlPlane.Api**：`CertManagerSettingsController`（GET/PUT/测试连接）、`CertificateEnrollmentsController`（列表/登记/手动同步/移除）；`Program.cs` 注册 Messaging（订阅两个事件）+ HttpClient；appsettings 增加 `Messaging` 节。
- **MinGo.DataPlane**：零改动。
- **frontend/min-go-console**：新增 `CertEnrollments.tsx` 页面 + 导航入口；`api/client.ts` 与 `types` 扩展。
- **外部依赖**：`MinGo.Messaging.Core/Hosting/SimpleMessageBroker`（nuget.org 已有 0.0.4/0.0.5，与 CertManager 版本对齐）；broker 复用环境现有 SimpleMessageBroker 实例（无新容器）；CertManager 与控制面两侧仅改配置。
- **兼容性**：新表迁移向后兼容；proto 零改动；人工上传证书流程完整保留，两种来源的证书在数据面行为一致；CertManager 侧无需代码改动（其事件已含所需字段，证书内容经下载接口获取）。
