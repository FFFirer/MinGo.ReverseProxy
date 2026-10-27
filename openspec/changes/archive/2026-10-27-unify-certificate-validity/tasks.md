## 1. 前置 spike（临时目录验证，代码不入库）

- [x] 1.1 实测 `ServerCertificateSelector` 返回 `null` 时 Kestrel 的失败形态：客户端收到 `unexpected eof while reading`（无 TLS alert），服务端只有 `Debug` 级别日志。结论记入 design §6.2；据此在 `SelectCertificate` 未命中分支补 `Warning`
- [x] 1.2 实测无 SNI 握手时 `domainName` 入参是空串（`""`，len=0），不是 `null`（design §6.1）；实现继续用 `string.IsNullOrEmpty`
- [x] 1.3 确认 `openspec/specs/data-plane-tls/spec.md` 与 `cert-sync-reload/spec.md` 基线未被其它在途 change 修改（`enable-https-redirect` 只新增 capability，无冲突）

## 2. 下发侧过滤与择优（控制面）

- [x] 2.1 `ConfigReplicationService.AddCertificatesToSnapshotAsync` 增加 `CertificateData != null && Length > 0` 过滤（下沉到 `CertificateSnapshotPolicy.SelectForDelivery`）
- [x] 2.2 按 `DomainName.Trim().ToLowerInvariant()` 分组：未过期组内取 `ExpiresAt` 最晚的一张；全部过期则取 `ExpiresAt` 最晚的一张；`ExpiresAt == null` 视为最晚（长期有效）；同刻按 `Id` 序数排序保证稳定
- [x] 2.3 确认**没有**任何按 `IsValid` 或 `ExpiresAt` 的排除逻辑 —— 过期证书必须留在快照里（design 4.1，`enable-https-redirect` 的 fail-closed 前提）
- [x] 2.4 没有复活 `MinGo.Infrastructure/Services/CertificateManager.cs` 的类；择优语义以纯函数 `MinGo.Core/Models/CertificateSnapshotPolicy.cs` 重新实现（该类已由 `cleanup-legacy-build-targets` 删除）

## 3. 快照顺序确定

- [x] 3.1 `ApiDbService.GetCertificatesAsync` 的 `ToListAsync()` 前加 `OrderBy(c => c.DomainName)`
- [x] 3.2 确认 `CertificatesController` 的三处调用不因排序变化而行为改变：列表只做展示派生；`/parse` 与 `/upload` 的重复检测按指纹全量扫描，与顺序无关

## 4. 握手选择（数据面）

- [x] 4.1 `SelectCertificate`：`domainName` 为 null/空 → 返回兜底证书（行为不变）
- [x] 4.2 精确匹配 → `*.` 通配匹配的顺序逐行不变；末行 `return _defaultCert` 改为 `return null`，并补一条 `Warning`（域名 + `No certificate configured`）
- [x] 4.3 spike 1.1 的失败形态可接受（EOF + 客户端无证书），已按实测在 spec 与 design §6.2 里写明，没有改成"回退一张名字不对的证书"
- [x] 4.4 `ReloadFromProvider` 加载完成后计算兜底证书：精确键 `localhost` 优先，否则缓存键字典序最小（通配键参与排序）
- [x] 4.5 兜底证书选定后记一条 `Information`，含域名与 thumbprint
- [x] 4.6 对 `expires_at_unix_ms` 已过期的证书记 `LogWarning`（域名 + 到期时间 `{ExpiresAt:O}` + `serving it anyway`），**照常**放入缓存，无拒绝路径
- [x] 4.7 加载判据仍是"bytes 能否解析"，未新增 `IsValid` 判断

## 5. 控制面 `IsValid` 口径

- [x] 5.1 `POST /api/certificates` 改为服务端按提交的 bytes 计算 `IsValid`（复用 private `ParseCertificate`），忽略客户端提交的值；无字节时记为 `false`
- [x] 5.2 列表与详情返回时用 `CertificateValidity.HasNotExpired(ExpiresAt, now)` 实时派生展示状态；存储列保持入库值不变（不加列、不加迁移）
- [x] 5.3 `CertificateConfig.IsValid` 注释写明"上传时刻的判定快照，不是当前状态"；时效判定收敛到 `MinGo.Core/Models/CertificateValidity.cs`
- [x] 5.4 确认 proto `is_valid` 继续填充但没有任何过滤消费者；`expires_at_unix_ms` 现在有数据面 Warning 这一个消费者（8.4 的 grep 对照）

## 6. 规格与文档

- [ ] 6.1 归档时把 delta 合入 `openspec/specs/data-plane-tls/spec.md`（两条 fallback 措辞改为握手失败、兜底确定性、过期照常呈现）
- [ ] 6.2 归档时把 delta 合入 `openspec/specs/cert-sync-reload/spec.md`，消除 "for all valid certificates" 这个无实现的措辞
- [x] 6.3 `enable-https-redirect` 确实新增了 `TryGetCertificate`。评估结论：**保留**。`SelectCertificate` 未命中会记 Warning（TLS 场景需要对握手失败留痕），明文跳转准入是高频探测路径且不应制造假告警；两者共用同一份 `TryFindInCache` 匹配逻辑，语义已收敛，只是日志策略不同

## 7. 测试

- [x] 7.1 数据面选择器：SNI 未命中 → 返回 `null` 且记一条 Warning（`DataPlaneCertificateSelectorTests.UnknownSni_ReturnsNull_AndLogsWarning`）
- [x] 7.2 数据面选择器：无 SNI（`null` 与 `""`）→ 返回兜底；存在 `localhost` 时兜底为它，否则字典序最小
- [x] 7.3 数据面选择器：`*.example.com` 匹配 `app.example.com`；`example.com` 不匹配通配且不回退
- [x] 7.4 数据面加载：过期证书进缓存 + 一条 Warning；空 bytes 跳过（同域名择优在 7.5 的策略层覆盖）
- [x] 7.5 控制面快照组装策略：空 bytes / 空域名被剔除；过期证书仍在；同域名只出现一张；重复组装顺序一致（`CertificateSnapshotPolicyTests`）
- [x] 7.6 控制面 `IsValid`：`POST /api/certificates` 的客户端值被服务端计算覆盖；纯计算部分由 `CertificateValidityTests` 覆盖。未新增控制面 API 测试项目 —— `build-targets` 规格把测试项目固定为两个
- [x] 7.7 真实 TLS 握手（`DataPlaneTlsHandshakeTests`，Kestrel + 真实选择器）：已知 SNI 握手完成；无 SNI 拿到兜底证书；错误 SNI 握手失败（`IOException`/`AuthenticationException`），覆盖 spike 1.1 的结论

## 8. 验收

- [x] 8.1 `dotnet build` 0 错误；`dotnet test` 全绿（Core 74 + DataPlane 54）
- [x] 8.2 `openspec validate unify-certificate-validity --strict` 通过
- [x] 8.3 无 SNI 仍能完成握手并拿到兜底证书 —— 由 `DataPlaneTlsHandshakeTests.NoSni_CompletesHandshakeWithDefaultCertificate` 在真实 Kestrel 上覆盖；完整的 compose 端到端检查合并到 `enable-https-redirect` 的联合验收
- [x] 8.4 全仓 grep 对照 spec 的三个词：唯一的"在时效内"计算在 `CertificateValidity`；`IsValid` 只出现在入库写入、展示派生、proto 透传，无过滤消费者；无任何按过期剔除的路径
- [x] 8.5 与 `enable-https-redirect` 联合验证：`HttpsRedirectE2ETests.ExpiredCertificate_StillRedirects` 在真实 Kestrel/YARP 上确认过期证书域名得到 308 且 `Location` 精确为 `https://expired.example.net:<port>/ping` —— 即不落明文代理。浏览器的 `ERR_CERT_DATE_INVALID` 由客户端证书校验产生（服务端不参与），E2E 里用回调放行才得以观察到后续请求，故该现象本身不作为断言
