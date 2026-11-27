# 证书自动接入 — 设计

## Context

**反代侧现状（MinGo.ReverseProxy）**

- 证书落库 `ApiCertificates`：`ApiCertificateEntity`（DomainName/CertificateType/CertificateData/Password/ExpiresAt/Thumbprint…），PFX 密码 DataProtection 加密，证书字节明文（`src/MinGo.Core/Entities/ProxyConfigEntities.cs:82-96`、`ApiDbService.cs:853-909`）。
- 证书只经人工上传（`CertificatesController`：POST/PUT/DELETE + parse/upload），无自动申请/续期；到期仅列表实时标 `IsValid` + 数据面日志告警（`CertificateExtensions.cs:168-178`）。
- 下发链路：证书 CRUD → ConfigUpdate 事件 → gRPC 快照广播（`ConfigReplicationService.BuildConfigSnapshotAsync` → `CertificateSnapshotPolicy.SelectForDelivery` 按快照路由域名交集过滤、同域名择优最晚到期者）→ 数据面 `ReloadFromProvider` 热更新。泛域名覆盖语义已实现（`WildcardCovers`：`*.example.com` 覆盖子域、不覆盖裸域）。
- HttpClient 基础设施：命名客户端 + `AddNamedHttpClients`（读 `HttpClient:Clients` 节）模式现成；单行设置表有 `CloudflareSettings` 先例（BaseUrl/Token 落库、API 读写）。
- 控制面目前**无任何** MinGo.Messaging 引用。

**CertManager 侧现状（调研结论，2026-11-27 代码核实）**

- 申请：`POST /api/external/certificates` `{Domain, IsWildcard, UseStaging}` → 建 Pending 记录并调度一次性 Quartz `CertificateRequestJob`（10 分钟超时），**接口返回即处理，无需外部发命令**（`ExternalApiController.cs:42-104`）；同域名在途申请返回 400。
- 状态：`GET /api/external/certificates/{id}/status`；下载：`GET /api/external/certificates/{domain}/download?format=Pfx|Pem|Crt&password=`，**password 为调用方传入**（仅 PFX 用），按域名返回最新有效证书（Active 且 ExpiresAt 最大、次按 IssuedAt），**无按 id 下载**。
- 认证：`Authorization: ApiKey <key>`，SHA-256 哈希比对（`ApiKeyAuthenticationMiddleware`）；ApiKey 需在 CertManager 管理 UI 创建，不自动播种。
- 事件（`MinGo.CertManager.Core/Messaging/`）：`CertificateCompletedEvent {CertificateId, Domain, ExpiresAt}`（`certmanager.certificate.completed`）、`CertificateFailedEvent {CertificateId, Domain, ErrorMessage}`（`certmanager.certificate.failed`），**均不含证书内容**。发布点：`CertificateService.ExecuteCertificateRequestAsync`（`CertificateService.cs:174-183/203-212`，`_messagePublisher != null` 时，即 Messaging 启用时）。注意 `CertificateRequestConsumer` 在命令路径**重复发布**同事件（`CertificateRequestConsumer.cs:37-71`）。
- 续期：Quartz 每 24h 扫描、到期前 30 天（`RenewalDaysBeforeExpiry`）走同一 `ExecuteCertificateRequestAsync`（isRenewal=true）→ 新建记录（RenewedFromId）→ 同样发 Completed 事件。
- 泛域名：单条记录（Domain=用户填写的裸域、IsWildcard=true），ACME 订单含 `domain + *.domain`（`AcmeService.cs:97-104`），PFX SAN 覆盖两者；下载按 `Domain == domain` 精确匹配。
- Messaging：`Messaging:Enabled=false` 默认关；SimpleMessageBroker transport（HTTP JSON 轮询，`BaseAddress`/`PollingInterval`/`BatchSize` 配置节），publisher 名 `CertBus`。
- 限制：DNS-01 仅阿里云；Let's Encrypt 限流适用（新域名每周 5 张）。

**MinGo.Messaging 现状**

- 纯 SDK：`MinGo.Messaging.Core/Hosting/SimpleMessageBroker` **nuget.org 公开**（0.0.4/0.0.5），topic=契约 Id、JSON camelCase 序列化。
- SimpleMessageBroker Server：独立 Web API（net10，SQLite/Postgres 持久化），单节点；**至多一次**（拉取即置 IsConsumed，ACK 仅校验）、无死信、无消费组过滤（其 SPEC 明确 Non-Goal）。每 topic 单订阅组场景不受组过滤缺陷影响。
- **环境现网（2026-11-27 实测）**：broker 已在运行——`http://100.102.18.111:5292` 返回 200（带 Dashboard 页面）；CertManager（`100.102.18.111:5042`）与控制面（`100.102.18.111:5000`）与其同处 Tailscale 网络。broker 宿主端口映射 5292→8080（`simple-message-broker` 仓库 compose，镜像 tag `0.0.2.e14965a`），未启用认证。**本变更不部署 broker**，只做发布/接收接线；两端部署仓库当前均未开启 Messaging（CertManager `Messaging:Enabled=false`），仅需配置变更。

## Goals / Non-Goals

**Goals**

1. 控制面登记域名后全托管：申请、同步、下发、续期同步全自动，人工仅首次登记。
2. 事件驱动为主路径（用户指定 MinGo.Messaging），对账轮询为兜底，保证「至多一次」传输下最终一致。
3. 自动导入证书与人工上传证书在下发链路完全同构（复用 `cert-sync-reload`）。
4. CertManager 侧零代码改动（其事件契约与下载接口已够用）。

**Non-Goals**

- 不改 CertManager（修其命令路径重复发布、补 HMAC 签名/限流均为后续独立事项）。
- 不在控制面做 ACME 客户端或 DNS-01（签发完全委托 CertManager）。
- 不做证书吊销自动化、不做多 CertManager 实例接入（单连接配置）。
- 不改 proto、不改数据面。
- 不解决 SimpleMessageBroker 本身的质量缺陷（用对账补偿；生产可切 RabbitMQ transport 作为备选，本变更不实施）。

## Decisions

### D1 拓扑与职责：控制面编排、CertManager 签发、broker 复用现网

```
用户 ──登记──> 控制面 ──POST /api/external/certificates──> CertManager（远程）
                ▲  │                                              │
                │  └── GET status / download (PFX) ──────────────┘
                │
          订阅 completed/failed 事件（HTTP 轮询）
                ▲
                │
   SimpleMessageBroker Server（环境现有，http://100.102.18.111:5292）
```

- 控制面只订阅不发布：申请经 External API POST 触发（CertManager 内部 Quartz 处理，无需 `RequestCertificateCommand`，规避其无域名字段的契约缺陷）。
- broker 已在环境运行且三方（控制面/CertManager/broker）同处 Tailscale 网络：发布方（CertManager）与订阅方（控制面）均以 `http://100.102.18.111:5292` 为 BaseAddress。跨机访问依赖 Tailscale 在线，为环境既有前提。
- 发布/接收接线细节见 D8；涉及 CertManager 的仅配置改动，无代码改动。

### D2 事件作信号、证书走 HTTP 拉取（不改契约）

- Completed/Failed 事件不含证书内容，仅携带 CertificateId/Domain/ExpiresAt——足够定位；证书内容经下载接口按域名拉取（该接口本就返回「最新有效证书」，续期后自然拿到新证）。
- Failed 事件 → 更新登记状态与错误信息，UI 可见、可手动重试。
- 幂等键：**(DomainName, Thumbprint)**。指纹相同跳过（不广播）；不同才导入。事件重复（CertManager 双发布）、对账重复、手动同步并发均被吸收。

### D3 登记语义：先探测、后申请

登记（POST 登记）时：
1. 校验 Domain 唯一（不区分大小写）与格式。
2. 调下载接口探测：有证书 → 直接同步（接入存量证书，无需重新申请，也避免 LE 限流）。
3. 无证书（404）→ POST 申请，记录 ExternalCertificateId，状态转 Pending，等事件/对账。
- 手动同步/对账只做「下载 + 指纹比对」，绝不重复 POST 申请（申请只发生在登记那一刻，续期由 CertManager 单方调度）。

### D4 数据模型与状态机

- `CertManagerSettings`（单行表，模式同 `CloudflareSettings`）：`BaseUrl`、`ApiKey`（DataProtection 加密，接口返回脱敏）、`Enabled`、`TimeoutSeconds`。
- `CertificateEnrollments`：`Id(Guid)`、`Domain`（唯一、归一化小写）、`IsWildcard`、`UseStaging`、`ExternalCertificateId(Guid?)`、`State`、`LastSyncAt`、`LastThumbprint`、`LastError`、`Enabled`、时间戳。
- 状态机：`Unrequested`（登记但未探测到/未申请）→ `Pending`（已申请，等完成）→ `Synced`（指纹同步成功）；任一环节失败 → `Failed`（可手动重试）；`Disabled`。
- 导入行复用 `ApiCertificates`：`CertificateType="Pfx"`、密码为**每次导入新生成的随机密码**（DataProtection 加密存储，与人工上传一致）。**泛域名导入两条行**：DomainName=`domain` 与 `*.domain`（同字节），与 `CertificateSnapshotPolicy.WildcardCovers` 语义对齐（裸域行覆盖裸域路由、通配行覆盖子域路由）。
- 指纹不同 → 新增行（不删旧行）：快照择优自动取新证（最晚到期优先），旧证过期后自然落选；列表页同域名多条可接受，与现有人工上传行为一致。清理策略（保留最近 N 张）记为后续可选优化。

### D5 同步编排：事件优先、对账兜底

- **事件消费者**（MinGo.Messaging `IConsumer<T>`，Competing 组）：Completed → 按 Domain 匹配登记（忽略未知域名的外部事件）→ 下载 PFX → 解析校验 → 指纹幂等导入 → 发布 ConfigUpdate（复用现有广播链路）→ 更新登记状态/指纹；Failed → 记录错误转 Failed。
- **对账工作器**（BackgroundService，默认每小时、可配）：遍历 Enabled 登记 → `GET status`（Pending 超时标记）+ 下载比对指纹 → 与事件消费者同一同步函数。覆盖：SMB 丢事件（至多一次）、broker/CP 宕机窗口、CertManager 重启丢一次性 Quartz 作业（`CertificateRequestJob` 不持久化，需对账发现 Pending 卡死并告警/提示手动重试）。
- 同步函数幂等可重入；导入与广播复用 `ApiManagementService`/`IApiDbService` 现有方法（或抽出共享私有方法），保证与人工上传同一条广播路径。

### D6 API 与前端

- `GET/PUT /api/cert-manager/settings`（PUT 保存时 ApiKey 若为空串/掩码则保持不变）、`POST /api/cert-manager/test`（连通测试：调 status 接口用随机 id，401=ApiKey 错、404=连通正常，据此给用户明确提示）。
- `GET /api/cert-enrollments`（含状态、到期时间、最近同步、关联本地证书数）、`POST`（登记，D3 流程）、`POST /{id}/sync`（手动同步）、`DELETE /{id}`（仅移除登记，不动已导入证书）。
- 前端新页面「证书接入」：顶部连接设置卡片，下方登记列表 + 登记表单（域名/泛域名/Staging）；状态徽标（Synced/Pending/Failed/Disabled）、最近同步时间、错误信息展示、手动同步与移除按钮。导航新增入口；沿用 toast、`client.ts` 错误提取等既有约定（含 SolidJS 信号竞态的已知坑规避）。
- 证书页（`Certificates.tsx`）不动；自动导入证书与人工证书同列表展示。

### D7 配置归属

- 业务连接配置（BaseUrl/ApiKey/Enabled）**落库**，经设置 UI 维护——与 `CloudflareSettings` 同模式，符合「配置默认值不进运行时」的项目惯例。
- broker 地址属基础设施差异，放两侧 appsettings/环境变量的 `Messaging` 节（与 CertManager 同构：`Enabled`/`Integrations:SimpleMessageBroker:BaseAddress/PollingInterval/BatchSize`），现网 BaseAddress = `http://100.102.18.111:5292`。
- CP 引 `MinGo.Messaging.Core/Hosting/SimpleMessageBroker` **0.0.4**（与 CertManager 对齐，序列化/契约行为一致；升级 0.0.5 留作独立评估）。

### D8 发布/接收接线（仅配置 + 订阅代码，无新部署）

**发布侧（CertManager，零代码改动）**——事件发布点已存在（`CertificateService.ExecuteCertificateRequestAsync` 在 `_messagePublisher != null` 时发布 Completed/Failed，申请与续期共用），只需在 `mingo-certmanager-deploy` compose 环境变量开启接线：

```
Messaging__Enabled=true
Messaging__Integrations__SimpleMessageBroker__BaseAddress=http://100.102.18.111:5292
# Publishers:CertBus:Integration=SimpleMessageBroker 及轮询默认值沿用 appsettings 既有配置
```

publisher `CertBus`、`PollingInterval`/`BatchSize` 默认值均已内置（`appsettings.json:111-128`），无需改写。注意 CertManager 容器需能出站访问 `100.102.18.111:5292`（Tailscale 网段内已验证可达；若容器网络策略限制可改用 `host.docker.internal:5292`，联调时确认其一）。

**接收侧（控制面）**——订阅两个事件，消费者触发现有同步编排（D5）：

- `Program.cs`：`AddMessaging().UseSimpleMessageBroker(...)` + `AddConsumers()`；注册 `CertificateCompletedEventConsumer` / `CertificateFailedEventConsumer`（`IConsumer<T>` + `[MessageConsumer(Contract=..., Group=..., DeliveryMode=Competing)]`），与 CertManager 的消费器写法同构。轮询由 `AddMessagingHost` 后台执行（默认 1s）。
- 控制面 appsettings 增加同构 `Messaging` 节（`Enabled=true`、`BaseAddress=http://100.102.18.111:5292`）。控制面**只订阅不发布**，不注册 publisher。
- 事件契约按契约 Id 匹配 topic（`certmanager.certificate.completed`/`.failed`）；消息反序列化依赖两端同样的 `MinGo.Messaging.Abstractions` 契约属性——CP 需在本仓库定义与 CertManager 完全一致的契约类（字段名/大小写一致，JSON camelCase）。
- 消费组规划：completed/failed 两 topic 仅控制面订阅者一组，CertManager 不订阅自身事件——不触及 SMB 的消费组过滤缺陷；每 topic 单组也规避组间竞争。

**安全**：现网 broker 未启用认证（compose 中 `Authentication__Enabled` 被注释）。Tailscale 私网内可接受起步；建议后续在 broker 启用 `Authentication__Enabled` + ApiKey 并在两侧 transport 配置 `ApiKey`（MinGo.Messaging 传输层已支持该字段），列为独立加固事项。

## 时序

**登记（含存量接入）**

```
用户 → POST /api/cert-enrollments
  → 校验域名唯一 → 存 Enrollment(Unrequested)
  → CertManagerClient.Probe(domain)：GET download（探测）
      ├─ 200 → 同步导入（指纹幂等）→ Synced + 广播
      └─ 404 → POST /api/external/certificates → 存 ExternalCertificateId → Pending
```

**申请完成（事件主路径）**

```
CertManager: CertificateRequestJob → ExecuteCertificateRequestAsync
  → 签发成功 → Publish CertificateCompletedEvent → SMB broker
CP: 消费者轮询到事件 → 匹配登记(by Domain) → GET download(Pfx, 随机密码)
  → 解析(指纹/到期) → 与 (Domain,Thumbprint) 比对
     ├─ 已存在 → 仅更新登记状态
     └─ 新指纹 → 导入 ApiCertificates（泛域名双行）→ ConfigUpdate → 快照广播 → Synced
失败 → CertificateFailedEvent → 登记 Failed + LastError
```

**续期（与申请同构）**

```
CertManager: CertificateRenewalJob（到期前30天）→ 新记录 → ExecuteCertificateRequestAsync(isRenewal)
  → Publish CertificateCompletedEvent(新 CertificateId, 同 Domain)
CP: 同上——按域名下载即得新证 → 指纹不同 → 导入新行 → 快照择优自动切新证
```

**对账兜底**

```
CP CertificateEnrollmentSyncWorker（每小时）：
  遍历 Enabled 登记 → GET status（Pending 超时告警）+ GET download 指纹比对 → 走同一同步函数
  → 事件丢失/broker 宕机窗口在 ≤1 小时内收敛
```

## Risks / Trade-offs

| 风险 | 对策 |
| --- | --- |
| **SMB 至多一次**：拉取即消费、ACK 非真确认、无死信 → 可能丢事件 | 对账工作器兜底（≤1h 收敛）；同步幂等；本设计不触及其消费组过滤缺陷（每 topic 单订阅组）。生产如需更强保证，MinGo.Messaging 支持切 RabbitMQ transport（仅换集成配置，本变更不实施）。 |
| **事件重复**：CertManager `CertificateService` 与 `CertificateRequestConsumer` 双发布 | CP 按 (Domain,Thumbprint) 幂等；命令路径本设计不走（不发布 RequestCertificateCommand），实际生效路径只发布一次。修 CertManager 重复发布列为后续独立事项。 |
| **CertManager 不可达/停机**：下载与状态查询失败 | 同步失败转 Failed + LastError（UI 可见）；对账持续重试；ApiKey 错（401）与网络不可达在连通测试中区分提示。 |
| **Let's Encrypt 限流**（新域名每周 5 张、失败重试有限） | 申请仅在登记时发生一次；同域名在途 400 直接映射为用户提示；存量证书走下载探测不触发申请；续期由 CertManager 单方调度、提前 30 天留足窗口。 |
| **ApiKey 泄露面** | 控制面 DataProtection 加密落库、接口脱敏；仅控制面后端出站调用；CertManager 侧建议启用 IP 白名单（其规划中 TODO，属对方变更）。 |
| **泛域名下载形态**：CertManager 记录 Domain=裸域，下载按精确匹配；SAN 含裸域+`*.domain` | 下载统一用登记裸域（已核实 `Domain == domain` 匹配）；导入拆双行覆盖两种 host 形态（D4）。首次联调需实测 PFX 解析后的 SAN 与指纹行为。 |
| **Pending 卡死**：`CertificateRequestJob` 一次性、不持久化，CertManager 重启会丢 | 对账发现 Pending 超时（如 >2h）→ 登记转 Failed 并提示「需手动重试」；重试=再次 POST 申请（同域名在途检查已清）。 |
| **时钟/时区**：ExpiresAt 两端口径 | 两端均 UTC（已核实），导入时直接沿用；`CertificateValidity` 既有判定口径不变。 |
| **broker 版本/协议漂移**：运行中镜像 tag `0.0.2.e14965a`，客户端包 0.0.4，而 CertManager 生产从未启用 Messaging——两者从未实际联调过 | 首个联调项：先开发布侧，验证事件写入 broker 与控制面拉取；若协议不兼容，升级 broker 镜像（MinGo.SimpleMessageBroker 现仓库版本），属既有环境资产升级，不在本变更交付范围。 |
| **broker 未启用认证**：现网 `Authentication__Enabled` 被注释，队列裸奔 | Tailscale 私网内起步可接受；加固路径见 D8（broker 开认证 + 两侧 transport 配 ApiKey），列为独立事项。 |
| **Tailscale 依赖**：BaseAddress 为 Tailscale 地址 `100.102.18.111` | 三方同处 Tailscale 网络是环境既有前提；若未来迁移公网接入，BaseAddress 改隧道地址即可（配置驱动，无代码影响）。 |
