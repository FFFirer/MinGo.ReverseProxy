## Why

"这张证书有效吗"在项目里同时存在**三套互不一致的活判定**，而且没有一处是运行时真正的决策者：

- `CertificatesController.cs:174`（`POST /api/certificates/upload`）：上传那一刻用 `DateTime.Now` 算出 `IsValid` 落库，此后永不重算 —— 证书过期后数据库里仍写着 `IsValid = true`。另一条写入路径 `POST /api/certificates`（`:38`）直接接受客户端提交的 `CertificateConfig`，`IsValid` 由调用方随口给。
- `ConfigReplicationService.cs:161-176`：下发**全量**证书，既不按 `IsValid` 过滤也不按 `ExpiresAt` 过滤。
- `CertificateExtensions.cs:63-88`：数据面加载时对 `is_valid` / `expires_at_unix_ms` **完全不看**，唯一判据是 `X509CertificateLoader` 抛不抛异常。

由此产生的四个具体故障：

1. **规格与实现不符**：`cert-sync-reload` 第 24-26 行声明快照 SHALL 包含 "all valid certificates"，实现是全量下发。规格里那个"valid"没有任何代码对应。
2. **快照里带着永远加载不出来的条目**：开发种子数据 `ApiDbService.cs:521-542` 的 `api.example.com` / `admin.example.com` 两张证书 `CertificateData` 为 `null`（`IsValid = true`），被无过滤地写进快照，数据面走 `CertificateBytes.IsEmpty → continue`（`:67`）静默丢弃。
3. **无 SNI 客户端拿到哪张证书不确定**：`_defaultCert ??= cert`（`:81`）取"本次循环里第一个成功加载的"，而 `GetCertificatesAsync`（`ApiDbService.cs:335`）是无 `WHERE`、无 `ORDER BY` 的 `ToListAsync()`。同一份数据库，重启两次可能给出不同的默认证书。
4. **同域名多张证书时结果取决于查询顺序**：`_certCache[key] = cert`（`:79`）后写覆盖，没有任何择优规则。原本做过择优的 `CertificateManager.cs:154-163`（同域名取有效期内最晚过期的一张）已核实**不可达** —— 只在 `src/MinGo.ReverseProxy/Kestrel/Extensions.cs:16-18` 注册，`InitializeCertificatesAsync`（`MinGo.Infrastructure/Extensions.cs:41`）全仓零调用者，控制面 DI 也不注册它；它随 `cleanup-legacy-build-targets` 一起消失，择优能力不回补就永久丢了。

同时，`enable-https-redirect` 把两件它明确不管的事交接过来（见该 change 的 Out of scope）：SNI 握手的 fail-closed 改造、证书过期语义。

## What Changes

把"有效"这个词拆成三件一直在混用的事，各自固定在唯一的使用位置：

- **可加载**（bytes 能变成 `X509Certificate2`）→ 只用于**下发过滤**：控制面把 `CertificateData` 为空的条目挡在快照之外，不再往下塞死条目
- **域名命中**（精确或 `*.` 通配）→ 只用于**握手选择**：数据面对**带 SNI** 的连接不再回退默认证书，匹配不到就是匹配不到
- **在时效内**（`NotBefore ≤ now ≤ NotAfter`）→ 只用于**告知**：数据面加载时对已过期证书记一条 Warning，照常呈现；控制面列表按 `ExpiresAt` 实时派生展示口径。**服务端不做过期拒绝** —— 让浏览器回 `ERR_CERT_DATE_INVALID` 比让握手直接断掉更可诊断，而且客户端无论如何都会拒绝，服务端拒绝没有安全收益

配套的四项小改动：

- **默认证书确定性化**：`_defaultCert` 收窄为"无 SNI 连接的兜底"，选取规则固定为「精确键 `localhost` 优先，否则域名字典序最小的一张」，并在加载日志里写明选了哪张
- **同域名择优**：下发时按域名分组，未过期的里取最晚过期的一张；全都过期则取最晚过期的一张（把随 `CertificateManager` 删掉的能力迁到 `ConfigReplicationService`，约 6 行，不复活那个死类）
- **快照内容确定**：`GetCertificatesAsync` 加 `OrderBy(c => c.DomainName)`
- **`IsValid` 列语义固定**为"上传时刻的判定快照"，只作为历史记录保留；`POST /api/certificates` 不再信任客户端提交的 `IsValid`，由服务端按提交的 bytes 计算

**零新增**：不加列、不加表、不加 EF 迁移、不加 proto 字段（`is_valid` / `expires_at_unix_ms` 已存在）、不加后台扫描服务、不加前端页面。

## Capabilities

### New Capabilities

- `certificate-validity`: "有效"的三词词汇表、控制面对 `IsValid` 的写入与展示口径、同域名择优规则

### Modified Capabilities

- `cert-sync-reload`: 快照的证书内容从"全量（含加载不出来的）"改为"仅可加载的、每域名一张、顺序确定"，并修正 "for all valid certificates" 这个无实现的措辞
- `data-plane-tls`: 带 SNI 的握手不再回退默认证书（**行为变更**）；默认证书确定性化；已过期证书照常呈现并记 Warning

## Impact

- **MinGo.ControlPlane.Api**：`GrpcServices/ConfigReplicationService.cs` 的 `AddCertificatesToSnapshotAsync` 加空 bytes 过滤 + 同域名择优；`Controllers/CertificatesController.cs` 的 `POST` 路径补服务端计算 `IsValid`，列表返回按 `ExpiresAt` 派生展示口径
- **MinGo.Infrastructure**：`Data/ApiDbService.cs:335` `GetCertificatesAsync` 加 `OrderBy`
- **MinGo.DataPlane**：`Kestrel/CertificateExtensions.cs` 的 `SelectCertificate` 去掉带 SNI 时的 `return _defaultCert`、`ReloadFromProvider` 加过期 Warning 并确定性设置 `_defaultCert`
- **测试**：数据面证书选择器单测（命中/未命中/通配/apex/无 SNI/同域名择优/过期呈现）、控制面快照组装单测
- **跨 change**：本 change 落地后 `SelectCertificate` 与 `enable-https-redirect` 计划的 `TryGetCertificate` 语义收敛为同一个 —— 若本 change 先上线，A 可以直接复用 `SelectCertificate`，省掉那个新方法
- **不动**：`MinGo.Shared` 死副本里的第四套判定（→ `cleanup-legacy-build-targets`）、`is_valid` / `expires_at_unix_ms` proto 字段本身（保留下发，语义在规格里写明"非策略输入"）

## Out of scope

- **过期主动告警的生产者**：`EventType.ErrorAlert` 有枚举（`GatewayEvent.cs:23`）和消费分支（`EventSubscriptionService.cs:87`），全仓找不到生产者。做成事件需要新的后台扫描服务，本 change 只补数据面加载时的 Warning 日志
- **ACME / 自动续期**
- **删除 `is_valid` proto 字段**：删字段要处理 tag 保留与 wire 兼容，收益为零
- **HSTS**（→ 单开 change）
- **`ForwardedHeaders`**：本拓扑数据面直连 8443，不需要
