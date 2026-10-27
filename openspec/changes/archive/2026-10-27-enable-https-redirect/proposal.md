## Why

数据面 `:8080` 明文与 `:8443` HTTPS 长期并存，任何到达 8080 的请求都以明文被服务并转发到上游。全仓 `UseHttpsRedirection` / `RequireHttps` / `ForwardedHeaders` 0 命中 —— 项目没有任何 HTTP→HTTPS 跳转能力，后端服务在明文下运行完全静默，运维无从察觉。

期望"默认启用 HTTPS 重定向"，同时允许部分域名继续走明文。参照 Caddy 的默认策略：自动 HTTPS 域名由内置规则返回 **308 Permanent Redirect**（保 method + body），HTTP listener 只做跳转，例外通过显式关闭表达。

## What Changes

- **默认启用 HTTPS 重定向**：数据面对已配置证书的域名，把明文请求以 **308** 重定向到 `https://{host}:{Port}{path}{query}`
- **准入 = 证书命中**：跳转的唯一条件是"精确或 `*.` 通配域名能匹配到证书"，**不回退到默认证书**（与现有 `SelectCertificate` 的唯一区别，见 design 3.1）。证书已过期**也照样跳**——跳向失败的 TLS 握手是预期结果，明文兜底不是
- **例外 = 不给该域名配证书**（Caddy 语义）：没证书的域名继续明文服务。另外提供一个纯配置项 `HttpOnlyHosts: []`（支持 `*.` 通配）作为"有证书但确实要走明文"的兜底口子，默认空数组
- **配置驱动的基建参数**：`appsettings.json` 新增 `HttpsRedirect` 节 —— `Enabled`（默认 `true`）、`Port`（默认 `8443`，含义是**对外可达端口**）、`HttpOnlyHosts`。`appsettings.Development.json` 覆盖 `Enabled: false`
- **内置豁免**：`/healthz/live`、`/healthz/ready` 永不跳转；Host 缺失或为空时不跳转（内置中间件对空 Host 会产出 `Location: https://:8443/`，见 design 3.8）
- **启动自检**：`Enabled = true` 而 `Port` 缺失或非法时抛异常拒绝启动 —— 内置中间件在端口未知时是 **fail-open**（放行明文 + 一条 warning），这个失败模式与"默认启用安全能力"不匹配（design 3.9）

**零新增**：不新建实体、表、EF 迁移、proto 字段、REST 控制器、前端页面。证书命中判定直接复用数据面已有证书缓存的键集合。

## Capabilities

### New Capabilities

- `https-redirect`: 明文请求的 HTTPS 重定向准入判定、308 永久跳转强度、健康检查与无效 Host 的豁免、启动配置自检

### Modified Capabilities

（无。`data-plane-tls` 的握手行为与三条 fallback Scenario 完全不变，本 change 只新增一个不回退的只读判定方法。）

## Impact

- **MinGo.DataPlane**：`appsettings.json` / `appsettings.Development.json` 新增 `HttpsRedirect` 节；新增 `Middleware/HttpsRedirectPolicy.cs`（准入判定 + 豁免判定，约 60 行）；`Program.cs` 在 `UseRouting()` 之前插入条件分支（`MapWhen` + 内置 `UseHttpsRedirection`，见 design 3.3）与启动自检；`CertificateExtensions.cs` 新增 `TryGetCertificate(domain)`（= 现有 `SelectCertificate` 去掉末行 `return _defaultCert`），**不改** `SelectCertificate`
- **docker-compose.local.yml**：data-plane environment 增加 `HttpsRedirect__Enabled=false`
- **docker-compose.yml**（生产）：无需改动，`Enabled` 默认 true；如需明文例外在此加 `HttpsRedirect__HttpOnlyHosts`
- **不动**：`SelectCertificate` 的默认证书回退、证书过期判定与 `is_valid` / `expires_at_unix_ms` 的消费（→ `unify-certificate-validity`）、`MinGo.Shared` 死副本与旧根 Dockerfile（→ `cleanup-legacy-build-targets`）

## Out of scope

以下明确推迟，各自单开 change：

- **HSTS**（`UseHsts` / `AddHsts` 完全不调用）——`HstsOptions.ExcludedHosts` 与 `HttpOnlyHosts` 形状同构，届时复用同一份配置
- **SNI 握手的 fail-closed 改造**：现在选不到匹配证书会静默使用 `_defaultCert`（"第一个成功加载的证书"，顺序不确定），浏览器看到的是"服务在跑但证书名字不对"。这是真缺陷，但属于证书管理域，与本 change 的跳转判定无关
- **证书过期主动告警**：`EventType.ErrorAlert` 有枚举和 switch 分支，但全仓找不到生产者。fail-closed 之后这个缺口的重要性上升，作为 `unify-certificate-validity` 的相邻议题
- **`ForwardedHeaders`**：本拓扑数据面直连 8443，不需要；将来出现前置 TLS 终结层时，跳转准入必须改为读 `X-Forwarded-Proto`，否则必然重定向死循环
