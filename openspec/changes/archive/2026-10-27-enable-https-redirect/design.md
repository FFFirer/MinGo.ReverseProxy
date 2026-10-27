## 1. Context

本 change 把"明文请求跳 HTTPS"落到数据面。设计取向是对标 Caddy（准入 = 证书、强度 = 308、例外 = 不配证书），并把改动面压到最小：复用数据面已有的证书缓存，不新建实体/proto/UI。

本文件分三段：`Decisions` 是决策；`Facts` 是 2026-10-26 临时 spike 的实测结论，支撑这些决策；`Risks` 记录已知弱点与拒绝路径；`Open Questions` 是实施前仍需验证的点。事实与决策分离，是为了让 spike 结论能随框架升级重新校准，而不必重读整段论证。

## 2. Goals / Non-Goals

**Goals**

- `:8080` 默认把已配证书域名的明文请求以 **308** 跳到 `:Port`，保留 method 与 body
- 未配证书的域名继续明文服务（满足"按域名可启动仅 http"）
- `:8443` 握手行为**逐字节不变**
- 端口配置缺失时拒绝启动（fail-fast）
- 健康检查、无效 Host 永不跳转
- 不新增实体、表、EF 迁移、proto 字段、控制器、前端页面

**Non-Goals**

- 不消费 `is_valid` / `expires_at_unix_ms`，不做证书过期判定（→ `unify-certificate-validity`）
- 不改 SNI 握手的 `_defaultCert` 回退
- 不引入 HSTS、不引入 `ForwardedHeaders`
- 不引入 `X-Forwarded-Proto` 支持、不支持多 HTTPS 端口
- 不改 `data-plane-tls` 现有三条 fallback Scenario

## 3. Decisions

### 3.1 准入判定复用证书缓存，但不回退

数据面 `DataPlaneCertificateSelector` 已经维护 `Dictionary<string, X509Certificate2> _certCache`（键 = 小写 DomainName，随快照重建），匹配顺序是精确 → `*.` 通配 → `_defaultCert`（[CertificateExtensions.cs:31,38,42,49](C:/Users/0sdf0/source/repos/MinGo.ReverseProxy/src/MinGo.DataPlane/Kestrel/CertificateExtensions.cs)）。

跳转判定复用同一套键与匹配顺序，但**砍掉最后那级回退**：新增 `TryGetCertificate(domain)`，未命中即视为不跳转。理由：跳转的语义是"这个域名有 HTTPS 服务"，默认证书回退只保证握手能完成，不代表该域名确实提供 HTTPS。`SelectCertificate` 一行不动，握手侧零影响。

**不做的事**：不把匹配逻辑抽成新的公共 `DomainMatcher` 组件 —— 现在两处共用（握手 + 跳转），改动收益不足以支撑新增抽象，等握手侧也改语义（C）时一并处理。

### 3.2 跳转强度固定 308

内置中间件默认 307，`RedirectStatusCode` 显式设为 `Status308PermanentRedirect`。308 保 method + body（YARP 转发大量 POST/PUT），语义匹配"该域名长期只走 HTTPS"。业界对标 Caddy：自动 HTTPS 域名的跳转同样是 308。

不给"按域名选强度"的余地：多一档就多一套配置、一处校验、一类缓存差异 bug。永久档不可回退是已知代价，见 5.1 / 5.2。

### 3.3 复用内置中间件：`MapWhen` 条件分支

```
if (httpsRedirectOptions.Enabled)
    app.MapWhen(NeedsRedirect, b => b.UseHttpsRedirection());   // NeedsRedirect: 见 3.4
app.UseRouting();          // 现有第 95 行，插桩点在它之前
...
app.MapReverseProxy();     // 第 98 行
```

不重写 URL 构造、端口解析、308 逻辑，全部交给框架。`MapWhen` 在 .NET 10 ref pack 存在（`MapWhenExtensions.MapWhen`）；内置 `HttpsRedirectionOptions` 在 10.0.2 只有 `HttpsPort` 与 `RedirectStatusCode` 两属性，且不存在 `AddRedirectExcluding` —— 这是"必须自己出判定函数"的框架约束，不是风格选择。

### 3.4 判定函数 `NeedsRedirect(HttpContext)`

按顺序命中即返回（短路顺序刻意先做零成本检查）：

1. `Enabled = false` → **不跳**（分支根本不注册）
2. `Request.IsHttps` → **不跳**
3. `Path` 命中豁免：`/healthz` 前缀（`live`、`ready` 两条都在，[Program.cs:78,83](C:/Users/0sdf0/source/repos/MinGo.ReverseProxy/src/MinGo.DataPlane/Program.cs)）→ **不跳**
4. `Request.Host.Host` 为 null/空，或是 IP 地址 → **不跳**（见 3.8 与 5.4）
5. `Host.Host` 命中 `HttpOnlyHosts`（精确 + `*.` 通配）→ **不跳**
6. `TryGetCertificate(host)` 命中 → **跳**
7. 其余 → **不跳**

### 3.5 配置形状

```json
// src/MinGo.DataPlane/appsettings.json
"HttpsRedirect": { "Enabled": true, "Port": 8443, "HttpOnlyHosts": [] }

// src/MinGo.DataPlane/appsettings.Development.json（该文件目前只有 Serilog 节）
"HttpsRedirect": { "Enabled": false }
```

`Port` 的语义是**对外可达端口**，不等于 Kestrel 监听端口。`Kestrel:Endpoints` / `ASPNETCORE_URLS` 决定证书与监听，`HttpsRedirect:Port` 只决定 Location 里出现什么值，二者独立，不是重复配置源。

**本地两条路径都要关**：`dotnet run` / F5 走 launchSettings 的 `http` profile（`ASPNETCORE_ENVIRONMENT=Development`，同时监听 8080+8443）由 `appsettings.Development.json` 覆盖；`docker-compose.local.yml` 的 data-plane 是 `ASPNETCORE_ENVIRONMENT=Production`（第 62 行），必须显式加 `HttpsRedirect__Enabled=false`。只改 Development 配置文件会漏掉 compose 路径，直接打断 `local-verify-infra` 的 `http://localhost:8080` 断言。

### 3.6 健康检查与 telemetry 的顺序

插桩点在 `UseRouting()`（第 95 行）之前、`MapHealthChecks` 注册之后。实测（4.6）：`MapHealthChecks` 只是登记端点，**不会**把 `/healthz/*` 提前摘出管道；`MapWhen` 分支位于端点中间件之前，因此 `/healthz/*` 默认也会进入跳转判定。3.4 第 3 条的显式豁免是**生效条件**，不是防御性冗余。

### 3.7 例外只用配置文件表达

`HttpOnlyHosts` 是配置项，不进数据库、不做 CRUD 接口。理由与 `cluster-name-is-id` 的既有取向一致：域名策略属于部署期声明的基建参数。将来若需要运行时按域名管理，再单开 change（届时才需要 proto 字段与实体，见本 change 的 Out of scope）。

配置列表与证书准入两条口子并存是有意的：证书准入覆盖"从未打算上 HTTPS"的域名，`HttpOnlyHosts` 覆盖"有证书但必须留明文"的少数内部管理域名。

### 3.8 IP 与无效 Host 一律不跳

内置中间件用 `Request.Host` 构造 Location。实测：Host 头带 `:8080` 甚至带 `:443` 都会被正确替换成 `HttpsPort`（design 4.1），但**完全没有 Host 头时会输出 `Location: https://:8443/`** —— 空主机名 + 端口是畸形 URI（design 4.3）。

判定为 IP 地址（IPv4 / IPv6 字面量）的 host 同样不跳：这类流量来自内网直连、监控探针、`curl` 类客户端，跟随跳转只会撞上 SNI 不匹配的自签错误。

### 3.9 启动自检 fail-fast

`Enabled = true` 且 `Port` 缺失或超出 1–65535 时抛 `InvalidOperationException` 拒绝启动。这是对框架 fail-open 的补偿：实测端口解析不出来时内置中间件只写一条 warning，随后**照常以明文返回 200**（design 4.2）。对以"默认启用 HTTPS"为前提的部署，静默放行明文是不可接受的失败模式。

## 4. 已实测事实（2026-10-26，临时 spike，代码未入库）

### 4.1 显式 `HttpsPort` 下，Host 头端口一律被替换

```
请求 Host: example.com:8080   → 307, Location: https://example.com:8443/ping
请求 Host: example.com:443    → 307, Location: https://example.com:8443/ping
请求 Host: 1.2.3.4            → 307, Location: https://1.2.3.4:8443/ping
query 与 path 原样保留
```

内置中间件已解决"端口拼接"问题，不需要自己构造 Location。

### 4.2 端口解析不出来时是静默放行

```
未配置 HttpsPort，仅 http 监听 → HTTP/1.1 200 OK
日志: Failed to determine the https port for redirect.
```

### 4.3 无 Host 头不会产出 Location

2026-10-26 记录为 `HTTP/1.1 307` + `Location: https://:8443/ping`；2026-10-27 用 `curl --http1.0 -H "Host:"` 复测，实际是 `308` 且**没有** `Location` 头。两次结论的共同点是"空主机名永远不该被跳转"，实现仍按 3.8 直接豁免空 Host，不依赖框架这一细节。

### 4.4 通配绑定不会破坏端口探测

`Uri.TryCreate("https://+:8443")` 返回 **false**，但 Kestrel 在 `IServerAddressesFeature` 中把 `+` 上报为 `https://[::]:8447`，框架仍解析得到端口（实测 B2 用例）。本 change 依赖显式 `Port`，不依赖此机制。

### 4.5 证书有效性判定当前有三套活判定且互不一致（本 change 不修）

> 2026-10-27 更新：`unify-certificate-validity` 已先落地，下列三处已被收敛为"可加载 / 域名命中 / 在时效内"三词各一处，`CertificateManager` 那份随 `cleanup-legacy-build-targets` 删除。保留本节是为了说明本 change 的准入为什么只看"键存在"。

- `CertificatesController`：`IsValid` 在入库时由服务端按 bytes 计算，列表按 `ExpiresAt` 实时派生；不再有"第二套当前状态"
- `ConfigReplicationService`：只按"可加载"（bytes 非空、域名非空）过滤，同域名择优，**不按** `IsValid` / 过期时间剔除
- `DataPlaneCertificateSelector`：加载判据是"bytes 能否解析"，过期只记 Warning；兜底证书按 `localhost` / 字典序确定

因此本 change 明确不引入第四套有效性判定：**准入只看"键存在"，不看有效期**（过期证书照样触发跳转，见 spec 场景）。

### 4.6 `MapWhen` 在 `UseRouting` 之前时，健康检查也会进分支

```
predicate = (_ => true)，插入在 UseRouting 之前
GET /healthz/live → 308, Location: https://127.0.0.1:8443/healthz/live
```

端点登记不会提前吞掉请求；3.4 第 3 条的 `/healthz` 豁免因此是必需的（见 3.6）。

### 4.7 `RedirectStatusCode = 308` 在 `MapWhen` 分支内生效

```
POST/GET Host: example.com:8080 → 308 Permanent Redirect
Location: https://example.com:8443/ping?a=1&b=2
Host: example.com:443           → https://example.com:8443/...   （端口被替换，不重复）
IP 直访 192.168.1.10:8080       → 308（所以 3.4 第 4 条必须显式豁免 IP）
```

### 4.8 YARP 的 host 匹配忽略端口

`match.host = api.example.com` 对 `Host: api.example.com:8080`（以及跳转后的 `:8443`）均命中；端到端测试里 POST 跟随 308 后仍由同一路由转发成功。`Request.Host.Host` 本身已剥离端口，判定函数不需要再手工剥离（`TryGetCertificate` 仍保留剥离逻辑以防被别处复用）。

## 5. Risks / Trade-offs

### 5.1 308 被缓存后无法回退

已访问过该域名的浏览器/客户端会缓存 308。之后即便从证书缓存中删除该域名，客户端仍直连 HTTPS。缓解：上线前确认证书来源稳定；真要回退只能等缓存过期或换域名。

### 5.2 首次部署即把全部已配证书域名永久跳转

默认 `Enabled = true` + `Port = 8443` + 已有证书 → 部署那一刻起所有证书域名的明文流量永久跳转。上线顺序必须显式确认，见 6.1。

### 5.3 fail-fast 收紧了启动条件

`Port` 缺失即拒绝启动。这是刻意的，但改变了"数据面只要配置能起来就起来"的既有预期，需在本地验证中确认两条路径（Development / compose）都不被误伤。

### 5.4 IP 直访与无 Host 永不跳转

`http://192.168.x.x:8080` 这类流量不会被跳。内网探针、监控、`curl` 直连 IP 属预期豁免；如果将来要求"任何流量都必须走 HTTPS"，需要另加机制（如监听层拒绝明文），不在本 change 范围。

## 6. Migration Plan

### 6.1 上线顺序

1. **本地**：`HttpsRedirect__Enabled=false`（compose）+ `appsettings.Development.json` 的 `Enabled:false`（dotnet run）→ 行为与现在完全一致，`local-verify-infra` 断言不受影响
2. **预发**：`Enabled=true`、`Port=8443`，`HttpOnlyHosts` 列出已知明文域名 → 证书域名的明文流量永久 308，**不可回退**
3. **回退**：只回退代码 + `Enabled=false`。已缓存 308 的浏览器仍打 HTTPS —— 由 8443 正常服务，无需额外处置，但必须知道"关闭开关不等于撤销客户端缓存"

## 7. 已关闭的 Open Questions（2026-10-27 实测）

### 7.1 `MapWhen` 分支的管道顺序与日志可见性 → 见 4.6

健康检查端点**不会**绕过跳转分支：端点登记只是数据源，分支在端点中间件之前执行。因此 3.4 第 3 条的显式豁免是生效条件。未命中证书的明文请求不会触发内置中间件的 warning（该中间件只在端口解析失败时告警），因此 fail-open 兜底仍只能依赖启动自检（3.9）。

### 7.2 YARP 的 host 匹配是否忽略端口 → 见 4.8

忽略端口，`Request.Host.Host` 也已剥离端口，判定函数无需额外处理；`TryGetCertificate` 的剥离逻辑是防御性复用，不是必要条件。

### 7.3 `RedirectStatusCode = 308` 在 `MapWhen` 分支内是否生效 → 见 4.7

生效。跳转响应为 `308 Permanent Redirect`，path/query 保留、端口被替换；端到端测试同时验证了 POST 跟随 308 后 method 与 body 原样到达上游。
