## 1. Context

数据面握手的证书选择是**唯一直接面向客户端的证书决策点**，目前它的输入来自三条互不相干的链路：控制面上传时的一次性判定（冻结在数据库）、下发的全量透传、数据面的"能不能加载"。规格里写的是 "all valid certificates"，代码里没有任何一处按"valid"过滤或拒绝。

本 change 不新增机制，只把已有的三处判定各自钉死在一个位置上，并把 `enable-https-redirect` 明确交接过来的握手侧行为改掉。

约束：
- 零 schema 变更（不加列、不加迁移）
- 零 proto 变更（`is_valid` / `expires_at_unix_ms` 已在 `dataplane.proto` 的 `CertificateData` 里）
- 必须与 `enable-https-redirect` 的 fail-closed 跳转语义兼容（见 4.1，这是硬契约）

## 2. Goals / Non-Goals

**Goals**
- "有效"只有一个词汇表，三个词各自有唯一的消费位置
- 带 SNI 的握手不再静默拿到一张名字不对的证书
- 无 SNI 的兜底证书跨重启稳定
- 同域名多张证书时有确定且合理的择优
- 快照里不再出现加载不出来的条目

**Non-Goals**
- 不做服务端过期拒绝（客户端会拒绝，且拒绝更可诊断）
- 不做过期事件告警的生产者
- 不动 `CertificateManager` 的删除方式（那是 `cleanup-legacy-build-targets` 的范围），只保证它删掉后择优能力不在本 change 里丢失
- 不动 `SelectCertificate` 的精确→通配匹配顺序本身

## 3. Decisions

### 3.1 三个词，三个位置

| 谓词 | 定义 | 唯一消费位置 |
|---|---|---|
| 可加载 | bytes 非空且 `X509CertificateLoader` 不抛 | 控制面下发过滤 + 数据面加载 |
| 域名命中 | SNI 精确匹配或小写 `*.` 通配匹配缓存键 | 数据面握手选择 |
| 在时效内 | `NotBefore ≤ now ≤ NotAfter` | 数据面加载日志 Warning + 控制面列表展示 |

此前它们被笼统称为"valid"，导致规格里那句 "for all valid certificates" 无法验证。

### 3.2 下发过滤只看"可加载"，不看时效

`AddCertificatesToSnapshotAsync` 增加 `CertificateData != null && Length > 0` 过滤，**不加** `IsValid` 过滤、**不加** `ExpiresAt > now` 过滤。理由见 4.1，这是与 `enable-https-redirect` 的硬契约。

### 3.3 带 SNI 不回退，无 SNI 才用兜底

`SelectCertificate` 现状（`CertificateExtensions.cs:28-50`）：入参为空 → `_defaultCert`；精确/通配命中 → 命中证书；否则 → `_defaultCert`。

改为：
- `domainName` 为 null/空（无 SNI）→ 兜底证书，**保留**
- 命中 → 命中证书，**保留**
- 有 SNI 且未命中 → 返回 `null`，握手失败

这不是把回退全删：无 SNI 的老客户端、`openssl s_client` 不带 `-servername`、健康探测用 IP 直连，仍然需要一张能完成握手的证书，否则连诊断入口都没有。收窄的那一类正是"客户端明确说了要找谁，却拿到别人的证书"。

未命中时由选择器自己记一条 `Warning`（域名 + `no certificate configured`）：Kestrel 只在 `Debug` 级别记录这次失败，生产默认 `Information` 下会完全静默（见 6.2）。这条日志是把 fail-closed 从"连接莫名其妙被断开"变成可定位问题的唯一手段。

### 3.4 兜底证书确定性规则

`_defaultCert` 不再取"循环里第一个成功的"，改为 `ReloadFromProvider` 完成后按固定规则计算：精确键 `localhost` 存在则用它，否则取缓存里域名字典序最小的一张。加载日志记一条 `Information` 写明兜底证书是哪个域名 + thumbprint。

选 `localhost` 优先是因为开发环境的自签证书就是 `localhost`（`data-plane-tls` 第 64-71 行），它作为无 SNI 兜底最不容易触发额外告警。

### 3.5 同域名择优迁到下发侧

`CertificateManager.cs:154-163` 的规则（组内优先未过期、按 `ExpiresAt` 最晚；全过期则按最晚）随 `cleanup-legacy-build-targets` 删除。本 change 在 `AddCertificatesToSnapshotAsync` 里以等价语义重新实现（按 `DomainName.ToLowerInvariant()` 分组，每组一张），约 6 行。

放在控制面而不是数据面：数据面 `_certCache[key] = cert` 的后写覆盖之所以危险，就是因为上游没保证每域名一张；在上游收敛，数据面保持现在的简单覆盖即可。

### 3.6 过期证书照常呈现

数据面加载时若 `expires_at_unix_ms` 已过期，记 `LogWarning("Certificate for {Domain} expired at {When}; serving it anyway")`，然后正常进缓存。不引入任何拒绝路径。

理由是 fail-closed 的**用户可诊断性**：`enable-https-redirect` 把 8080 跳向 8443，若握手侧因过期直接失败，运维看到的只有 `SSL_ERROR`；照常呈现则浏览器给 `ERR_CERT_DATE_INVALID` 并写明到期日期，直接指向"该续期了"。服务端加过期检查没有安全收益（客户端无论如何都会拒绝），只有诊断成本。

这一条同时给 `expires_at_unix_ms` 第一个消费者，`is_valid` 保持无消费者并在规格里写明"历史元数据，非策略输入"。

### 3.7 `IsValid` 列不动，只固定语义

列保留、类型不变、无迁移。语义固定为"上传时刻的判定快照"，不再声称当前有效。`POST /api/certificates`（`CertificatesController.cs:38`）当前直接落客户端提交的值，改为服务端按提交的 bytes 计算（复用已有的 private `ParseCertificate`）；列表与详情返回时，展示口径按 `ExpiresAt` 实时派生。

## 4. Risks / Trade-offs

### 4.1 与 `enable-https-redirect` 的硬契约（必须双向锁住）

A 的跳转准入是"域名命中缓存，不回退"，并且明确"证书已过期也照样跳"。这要求本 change 的下发**不能**按过期剔除证书：一旦剔除，过期域名的键就不在缓存里，A 判定为不跳转 → 该域名回到明文服务，fail-closed 语义**反转成 fail-open**。

因此本 change 的两条边界是 A 正确性的前提：下发只按"可加载"过滤（3.2）、过期证书照常进缓存（3.6）。两个 change 的 spec 里都要有对应措辞，实现顺序无要求。

### 4.2 带 SNI 不回退是行为变更（BREAKING）

单证书部署下，用另一个域名的 Host 去访问 8443，之前拿到那张唯一证书（握手成功、证书名不对），现在握手失败。这是有意的：名字不对的握手成功本来就不是可用状态，只是把错误从 TLS 层挪到了 HTTP 层（YARP 按 host 匹配路由，未匹配的路由本来就 400）。

上线前必须确认所有需要走 HTTPS 的域名都有对应证书（见 6.1）。

### 4.3 兜底证书会变

现状是"第一个成功加载的"，改成 `localhost` 优先 / 字典序最小，无 SNI 客户端看到的证书可能换一张。因为现状本身不确定，这不是回归；但如果有人依赖"恰好是某张"，需要显式配一张 `localhost` 证书来表达。

### 4.4 呈现过期证书 vs 用户说的"证书失效则请求失败"

字面上"请求失败"可以由握手失败或客户端拒绝任一种实现。本 change 选后者（3.6）：请求确实失败（浏览器拦截），但失败原因可读。如果之后需要服务端也拒绝，只需要在 `SelectCertificate` 的命中分支加一个时效判断，不影响本 change 的其他决策。

## 5. Migration Plan

### 5.1 数据面缓存语义变化的部署顺序

1. 控制面先发（下发过滤 + 择优 + `OrderBy`）：此时数据面行为不变，只是快照里少了空 bytes 条目、每域名一张
2. 数据面后发（不回退 + 兜底确定性 + 过期 Warning）：这一步是行为变更点，需要与 5.2 的证书覆盖检查一起完成
3. 观察数据面日志里的 `serving it anyway` Warning 与握手失败计数

### 5.2 上线前检查

对每个已在 YARP 路由里出现的 host，确认控制面证书表中存在同域名的可加载证书（精确或覆盖它的 `*.` 通配）。缺证书的域名在 A 上线前会走明文，A 上线后仍走明文（准入不命中），不会因本 change 变成握手失败 —— 除非有人手动拿那个 host 去连 8443。

## 6. 已实测事实（2026-10-27，临时 spike，代码未入库）

spike：`net10.0` 最小 Web 应用，Kestrel `127.0.0.1:9443` TLS，`ServerCertificateSelector` 对 `known.local` 返回证书、对其余非空名字返回 `null`、对空名字返回兜底证书；用 OpenSSL 3.2.1 `s_client` 探测。

### 6.1 无 SNI 时入参是空字符串，不是 null

```
[SELECTOR] raw="known.local" len=11
[SELECTOR] raw="unknown.local" len=13
[SELECTOR] raw="" len=0
```

无 SNI 连接的 `domainName` 是 `""`。现有 `string.IsNullOrEmpty(domainName)` 判断本来正确，改造后必须继续用它，不能写成 `domainName is null`。

### 6.2 返回 null 时服务端只记 Debug，客户端收到 EOF 而非 TLS alert

| 探测 | 客户端观感 | 服务端日志 |
|---|---|---|
| `-servername known.local` | `subject=CN=known.local`，握手完成 | Debug：`established using the following protocol: Tls13` |
| `-servername unknown.local` | `unexpected eof while reading` + `no peer certificate available`，**没有 alert** | **Debug**：`HttpsConnectionMiddleware[1] Failed to authenticate HTTPS connection. AuthenticationException: The server mode SSL must use a certificate with the associated private key.` |
| 无 `-servername` | `subject=CN=localhost`，握手完成 | Debug：握手完成 |

两条结论直接影响实现：

1. **fail-closed 后数据面在默认日志级别下看不见它**：Kestrel 把这次失败记在 `Debug`，生产默认 `Information` 会静默。因此 3.3 的"未命中返回 null"必须由我们自己在 `SelectCertificate` 里补一条 `Warning`（域名 + "no certificate configured"），否则 `enable-https-redirect` 跳过来的一批客户端全静默失败、运维无从定位。
2. **客户端拿到的是 EOF 而不是 `unrecognized_name` alert**：4.2 里"运维会看到握手失败"要写得准确——他们看到的是连接被关闭。这也是 3.6 让过期证书照常呈现的理由的延伸：能给出可读错误的路径优先。

### 6.3 通配键有资格当无 SNI 兜底

规则里"字典序最小"是在缓存键集合上取的，键包含 `*.example.com` 这类通配键，因此通配证书可以成为兜底；只有存在精确 `localhost` 键时才被压过。这一点写进了 `data-plane-tls` delta 的 Requirement：`*.` 键参与排序，避免实现时随手排除成"只允许非通配"。

### 6.4 多张证书覆盖同一域名的现状

`ApiDbService` 的证书表没有 `DomainName` 唯一索引（择优因此必须存在，不能假设只有一张）。择优后数据库里仍可能留多张，UI 上看不出哪张被下发了 —— 是否在列表里标注"当前生效"，留给实施时判断，不在本 change 的 spec 要求里。
