# 证书自动接入（对接 MinGo.CertManager）— 实施任务

> 设计依据：`proposal.md` / `design.md`。broker 复用环境现网（`http://100.102.18.111:5292`），本变更不部署 broker。

## 1. Core：实体 / 枚举 / DTO / Messaging 契约

- [x] 1.1 `Entities/CertManagerEntities.cs`：`CertManagerSettingsEntity`（单行，Id=`default`：BaseUrl/ApiKey/Enabled/TimeoutSeconds/UpdatedAt）、`CertificateEnrollmentEntity`（Domain 唯一不区分大小写、IsWildcard/UseStaging、ExternalCertificateId(Guid?)、State、LastSyncAt/LastThumbprint/LastError、Enabled、时间戳）
- [x] 1.2 `Models/CertEnrollmentModels.cs`：`CertificateEnrollmentState` 枚举（Unrequested/Pending/Synced/Failed/Disabled）、`CertificateEnrollmentDto`、`CertManagerSettingsDto`
- [x] 1.3 `Messaging/CertificateEvents.cs`：与 CertManager 同构契约类 `CertificateCompletedEvent`（Id=`certmanager.certificate.completed`，CertificateId/Domain/ExpiresAt 为 DateTime）、`CertificateFailedEvent`（Id=`certmanager.certificate.failed`，含 ErrorMessage）——JSON camelCase、字段名完全一致
- [x] 1.4 接口：`Interfaces/ICertManagerSettingsService.cs`（GetAsync 脱敏/GetWithApiKeyAsync/SaveAsync）、`Interfaces/ICertificateEnrollmentStore.cs`（列表/单查/创建/更新/删除）
- [x] 1.5 纯策略助手 `Models/CertificateEnrollmentPolicy.cs`：域名归一化、导入行规划（泛域名→`domain`+`*.domain` 双行）、(DomainName,Thumbprint) 幂等判定——便于单测

## 2. Infrastructure：表映射 / 迁移 / Client / 仓储

- [x] 2.1 `AppDbContext` 注册 `CertManagerSettings`、`CertificateEnrollments` 两个 DbSet + 实体配置（Domain 唯一索引）
- [x] 2.2 EF 迁移 `AddCertManagerEnrollment`（dotnet ef migrations add，含唯一索引）
- [x] 2.3 `Data/CertManagerSettingsService.cs`：单行读写，ApiKey 用 `IDataProtectionProvider` 加密存储（沿用证书密码模式）、脱敏返回、掩码值不回写
- [x] 2.4 `Data/CertificateEnrollmentStore.cs`：实现 `ICertificateEnrollmentStore`
- [x] 2.5 `Services/CertManagerClient.cs`：IHttpClientFactory 命名 "CertManager"；`RequestCertificateAsync`/`GetStatusAsync`/`DownloadPfxAsync`/`ProbeAsync`（404 判定无证书）/`TestConnectionAsync`（401=ApiKey 错、404=连通）；`Authorization: ApiKey <key>`；`CertManagerException(Status)` 异常模型

## 3. Application：登记服务 / 对账工作器 / 事件消费者

- [x] 3.1 `Services/CertificateEnrollmentService.cs`：
  - `RegisterAsync`（D3：域名唯一校验→探测下载→有证书直接同步/404 才 POST 申请→Pending）
  - `SyncAsync`（下载 PFX（随机密码）→X509 解析→`CertificateEnrollmentPolicy` 规划导入行→(DomainName,Thumbprint) 幂等→经 `IApiManagementService.CreateCertificateAsync` 入库触发广播→更新登记状态/指纹/到期）
  - `HandleCompletedAsync`/`HandleFailedAsync`（按 Domain 匹配登记，未知域名忽略）
  - `ListAsync`/`DeleteAsync`（仅移除登记，不动已导入证书）
- [x] 3.2 `Workers/CertificateEnrollmentSyncWorker.cs`：BackgroundService + PeriodicTimer（默认 1h，appsettings `CertManager:SyncIntervalMinutes` 可配），对账=遍历 Enabled 登记走 `SyncAsync`，错误记 LastError 不抛出
- [x] 3.3 `Consumers/CertificateEventConsumers.cs`：`[MessageConsumer(Contract=typeof(...), Group="cert-enrollment-cp", DeliveryMode=Competing)]` + `IConsumer<T>`；Completed→HandleCompletedAsync 后触发同步，Failed→HandleFailedAsync

## 4. ControlPlane.Api：控制器与接线

- [x] 4.1 `Controllers/CertManagerController.cs`（`api/cert-manager`）：GET/PUT settings（掩码不回写）、POST test（返回连通性结论：连通/ApiKey 错误/网络不可达）
- [x] 4.2 `Controllers/CertEnrollmentsController.cs`（`api/cert-enrollments`）：GET 列表、POST 登记、POST `{id}/sync`、DELETE `{id}`
- [x] 4.3 `Program.cs`：命名 HttpClient "CertManager"；注册 `ICertManagerSettingsService`/`ICertificateEnrollmentStore`/`CertificateEnrollmentService`/`CertificateEnrollmentSyncWorker`（HostedService）；`Messaging:Enabled` 时 `AddMessaging(Configuration).UseSimpleMessageBroker().AddConsumers(...)` + `AddMessagingHost()`
- [x] 4.4 `appsettings.json`：新增 `Messaging` 节（Enabled/BaseAddress=`http://100.102.18.111:5292`/轮询默认）与 `CertManager` 节（SyncIntervalMinutes）
- [x] 4.5 `MinGo.ControlPlane.Api.csproj`：引用 `MinGo.Messaging.Core/Hosting/SimpleMessageBroker` 0.0.4

## 5. 前端：证书接入页

- [x] 5.1 `types/index.ts`：CertManagerSettings、CertificateEnrollment、EnrollmentState
- [x] 5.2 `api/client.ts`：settings get/put/test、enrollments list/register/sync/remove
- [x] 5.3 `pages/CertEnrollments.tsx`：连接设置卡片（BaseUrl/ApiKey/测试连接/启用）+ 登记列表/表单/状态徽标/手动同步/移除；SolidJS 信号竞态规避（值绑定依赖选项信号）
- [x] 5.4 `App.tsx` + `Sidebar.tsx`：导航新增「证书接入」入口（`/cert-enrollments`，fa-certificate 图标，位于「证书管理」之后）

## 6. 测试与验证

- [x] 6.1 单测：`CertificateEnrollmentPolicyTests`（Core，18 例：域名归一化/泛域名双行/(DomainName,Thumbprint) 幂等/域名合法性）、`CertificateEnrollmentServiceTests`（CP，19 例：登记探测分流/申请失败落 Failed/同步双行导入/同指纹跳过/坏 PFX 落 Failed/事件处理/删除，mock ICertificateEnrollmentStore/ICertManagerClient/IApiManagementService）
- [x] 6.2 `dotnet build MinGo.ReverseProxy.slnx` 0 错误
- [x] 6.3 `dotnet test MinGo.ReverseProxy.slnx` 全绿：476/476（Core 197 + CP 133 + DP 146；既有 439 + 新增 37）
- [x] 6.4 `cd frontend/min-go-console && pnpm build` 通过

## 7. 部署侧（本变更外，实施时同步落）

- [ ] 7.1 `mingo-certmanager-deploy` compose：`Messaging__Enabled=true`、`Messaging__Integrations__SimpleMessageBroker__BaseAddress=http://100.102.18.111:5292`
- [ ] 7.2 控制面部署环境变量：`Messaging__Enabled=true` + 同 BaseAddress
- [ ] 7.3 联调验证：CertManager 侧 ApiKey 创建；事件写入 broker 与控制面拉取；泛域名 PFX 解析 SAN 行为
- [ ] 7.4 DataProtection 密钥环持久化（代码已就绪：`DataProtection:KeysPath` 配置节 + compose `DataProtection__KeysPath=/app/data/keys`）。**迁移注意**：新密钥环为空，切换前旧容器加密的值（带密码的证书、CertManager ApiKey）将无法解密——两者都有「解密失败按明文处理」的兜底，但结果会错：证书密码变成密文垃圾、ApiKey 401。零中断做法：`docker cp <cp容器>:/root/.aspnet/DataProtection-Keys/. ./data/keys/` 先搬旧密钥环再发新版；否则部署后在「证书接入」页重存 ApiKey，带密码证书重新上传
