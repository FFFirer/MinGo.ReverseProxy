## 1. 前置 spike（临时目录验证，代码不入库）

- [x] 1.1 `MapWhen` 分支内健康检查**不会**自动绕过：predicate 恒真时 `/healthz/live` 也得到 308（design 4.6）。因此 3.4 第 3 条的显式豁免是生效条件；未命中证书不会触发内置中间件 warning（它只在端口解析失败时告警），fail-open 兜底仍依赖启动自检
- [x] 1.2 YARP host 匹配忽略端口：`match.host = api.example.com` 命中 `Host: api.example.com:8080` 与 `:8443`；端到端 POST 跟随跳转后仍由同一路由转发成功（design 4.8）
- [x] 1.3 `MapWhen` 分支内 `RedirectStatusCode = 308` 生效；Location 保留 path/query 并把端口替换成 `HttpsPort`（design 4.7）

## 2. 配置与选项

- [x] 2.1 `src/MinGo.DataPlane/appsettings.json` 新增 `HttpsRedirect: { "Enabled": true, "Port": 8443, "HttpOnlyHosts": [] }`
- [x] 2.2 `src/MinGo.DataPlane/appsettings.Development.json` 新增 `"HttpsRedirect": { "Enabled": false }`
- [x] 2.3 新增 `Middleware/HttpsRedirectOptions.cs`（`Enabled` / `Port` / `HttpOnlyHosts`），`Validate()` 在 `Enabled = true` 且 `Port` 不在 1–65535 时抛 `InvalidOperationException`；`Program.cs` 启动即调用（等价于 `ValidateOnStart`，但异常类型符合 spec）

## 3. 证书只读判定

- [x] 3.1 `DataPlaneCertificateSelector` 新增 `TryGetCertificate(string? host)`：先小写并剥离端口（`NormalizeHost`）
- [x] 3.2 匹配顺序与 `SelectCertificate` 一致（精确 → `*.` 通配后缀），**没有**默认证书回退；与 `SelectCertificate` 共用 private `TryFindInCache`，但未命中不写日志
- [x] 3.3 `SelectCertificate` 的精确 → 通配顺序保持逐行不变；`unify-certificate-validity` 已把末行回退改为 `null` 并移除 `_defaultCert ??= cert`，本 change **未再改动**握手的任何判定，`data-plane-tls` 行为符合 C 落地后的规格

## 4. 跳转判定

- [x] 4.1 新增 `src/MinGo.DataPlane/Middleware/HttpsRedirectPolicy.cs`：`static bool NeedsRedirect(HttpContext, HttpsRedirectOptions, DataPlaneCertificateSelector)`
- [x] 4.2 按 design 3.4 的短路顺序实现：开关 → `IsHttps` → 豁免路径 → Host 有效性 → 配置例外 → 证书命中
- [x] 4.3 豁免 `/healthz` 前缀（`Path.StartsWithSegments`；`/healthzfoo` 不受影响）
- [x] 4.4 Host 为空、`localhost`、IPv4/IPv6 字面量一律不跳
- [x] 4.5 `HttpOnlyHosts` 精确 + `*.` 通配、大小写不敏感；`*.example.com` 不匹配裸域名

## 5. 管线插桩

- [x] 5.1 `builder.Services.AddHttpsRedirection`：`HttpsPort = options.Port`、`RedirectStatusCode = 308`
- [x] 5.2 启动自检：`Enabled = true` 且 `Port` 缺失/非法 → `InvalidOperationException`，进程非零退出（进程级实测：`HttpsRedirect__Port=0` 抛异常且立即退出）
- [x] 5.3 `app.UseRouting()` **之前**插入 `if (enabled) app.MapWhen(NeedsRedirect, b => b.UseHttpsRedirection())`
- [x] 5.4 `MapHealthChecks` 端点未被跳转：进程级实测 Development / Production / `HttpsRedirect__Enabled=false` 三种启动下 `GET /healthz/live` 均返回 200

## 6. compose 与文档

- [x] 6.1 `docker-compose.local.yml` data-plane environment 追加 `- HttpsRedirect__Enabled=false`
- [x] 6.2 `docker-compose.yml` data-plane 保持默认开启；在 ports 注释处说明 `HttpsRedirect:Port`（对外端口）与 `ASPNETCORE_URLS`（监听）职责不同
- [x] 6.3 更新 `docs/技术设计方案.md` 的 8080/8443 端口描述，写明 308 跳转与 `HttpsRedirect` 开关

## 7. 测试

- [x] 7.1 判定单测（`HttpsRedirectPolicyTests`）：有证书跳转 / 带端口跳转 / 大小写 / 通配命中 / 过期证书仍跳转 / 无证书不跳 / 裸域名不被通配 / `HttpOnlyHosts` 精确与通配 / `localhost` / IPv4 / IPv6 / 空 Host / 已 HTTPS / `/healthz*` 不跳 / `/healthzfoo` 正常跳 / `Enabled=false` 全放行
- [x] 7.2 端到端（`HttpsRedirectE2ETests`，真实 Kestrel HTTP+HTTPS + 选择器 + YARP）：308 + 精确 `Location`（path/query 保留、端口替换）；POST 跟随 308 后 method/body 到达后端；通配与过期证书仍跳转；`HttpOnlyHosts` 与无证书域名明文转发；`/healthz/live` 不跳转
- [x] 7.3 fail-fast：`HttpsRedirectOptionsTests` 覆盖 0 / -1 / 65536 抛异常、关闭时不校验；进程级实测 `HttpsRedirect__Port=0` 启动即失败；`dotnet build` 与测试全绿

## 8. 验收

- [x] 8.1 `openspec validate enable-https-redirect --strict` 通过
- [x] 8.2 本机 8080 被 `gitea-mcp-server` 容器占用，compose 全链路无法原样起（端口冲突）。以等价的双路径进程级验证代替：Development（文件覆盖）与 Production + `HttpsRedirect__Enabled=false`（compose 同款环境变量）下 `/healthz/live` 均 200、证书域名均不跳转，明文代理路径不受影响
- [x] 8.3 例外语义：`CertHost_IsRedirected` 与 `HostWithoutCertificate_IsNotRedirected` 使用同一选择器实例，覆盖"删证书后跳转消失 / 加回后恢复"；"已缓存的 308 不受开关影响"作为已知代价记录在 design 5.1
