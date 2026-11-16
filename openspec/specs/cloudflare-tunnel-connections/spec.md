# Cloudflare Tunnel Connections

## Purpose

网关支持把 Cloudflare Tunnel 作为与本地数据面并列的「接入通道」：隧道经 Cloudflare API 在控制面内完成增删改查，路由通过 `ConnectionId` 选择发布到本地数据面或某个隧道；发布到隧道的路由由控制面对账到该隧道的 ingress 规则与 DNS 记录（网关回源模式，流量经隧道 → 本地数据面）。Cloudflare 凭据仅由配置提供（`Cloudflare:ApiToken` / `AccountId`），不落库、不回显。

## Requirements

### Requirement: 路由 SHALL 通过 ConnectionId 选择接入通道

`RouteConfig.ConnectionId` SHALL 取值为 `local`（内置数据面，`ConnectionIds.Local` 常量）或某个 Cloudflare 隧道 ID。空值或空白 SHALL 视为 `local`。新增路由默认 SHALL 为 `local`，现有路由在升级迁移后 SHALL 保持 `local`，数据面（YARP）SHALL 忽略该字段、继续处理本地流量。

#### Scenario: 新建路由默认走本地数据面

- **WHEN** 用户在路由表单中不修改接入通道
- **THEN** 保存的 `ConnectionId` 为 `local`，路由照常发布到数据面

#### Scenario: 历史路由升级后仍走本地

- **WHEN** 数据库升级应用 `AddRouteConnectionId` 迁移
- **THEN** 已有路由的 `ConnectionId` SHALL 为 `local`，行为与升级前一致

### Requirement: Cloudflare 凭据 SHALL 由配置驱动且不回显

Cloudflare 访问凭据（API Token、AccountId）SHALL 只从 `Cloudflare` 配置节（appsettings / 环境变量）读取，SHALL NOT 存入数据库，任何 API 响应与页面 SHALL NOT 回显 Token。未配置 Token 时，接入通道页面 SHALL 显示配置指引（所需权限：Account:Cloudflare Tunnel:Edit、Zone:DNS:Edit、Zone:Zone:Read），隧道接口 SHALL 返回 503 而不是报错泄漏内部细节。

#### Scenario: 未配置凭据

- **WHEN** `Cloudflare:ApiToken` 为空且前端调用隧道相关接口
- **THEN** 接口 SHALL 返回 `503` 与「Cloudflare 不可用」说明
- **AND** 接入通道页面 SHALL 展示配置指引而非报错堆栈

#### Scenario: 凭据不出现在任何输出中

- **WHEN** 用户打开接入通道页面或调用状态接口
- **THEN** 响应与页面 SHALL 只包含账号名、区域数、隧道资源信息，不包含 Token 原文

### Requirement: 隧道资源 SHALL 经 Cloudflare API 实时管理

隧道 SHALL 不建本地表。控制面 SHALL 通过 Cloudflare API 提供隧道的查询、创建、重命名、删除；创建时 SHALL 使用 `config_src: cloudflare`（远程管理）。删除隧道时若存在绑定该隧道的路由，SHALL 返回 `409` 并列出冲突路由。

#### Scenario: 删除仍被路由引用的隧道

- **WHEN** 某路由的 `ConnectionId` 指向隧道 T 且用户请求删除 T
- **THEN** 接口 SHALL 返回 `409`，T SHALL 保持存在

#### Scenario: 删除未被引用的隧道

- **WHEN** 无任何路由绑定隧道 T 且用户请求删除 T
- **THEN** T SHALL 经 Cloudflare API 删除并从接入通道列表消失

### Requirement: 发布到隧道的路由 SHALL 强制配置匹配域名

`ConnectionId` 非 `local` 的路由 SHALL 必须配置 `match.host`，且该 host SHALL 通过域名语法校验（支持 `*.` 通配符，拒绝 scheme、路径、端口）。保存时后端 SHALL 校验隧道在 Cloudflare 上存在，且同一 host 不得绑定到不同隧道。

#### Scenario: 隧道路由缺少匹配域名

- **WHEN** 用户为路由选择隧道但未填写匹配域名
- **THEN** 前端表单 SHALL 提示「发布到隧道的路由必须配置匹配域名」且不提交

#### Scenario: 同一域名绑定到不同隧道

- **WHEN** 路由 A（绑定隧道 T1）已占用 `api.example.com`，用户把路由 B 的 host 也设为 `api.example.com` 并绑定 T2
- **THEN** 保存 SHALL 返回 `400`，说明域名已被 T1 占用

### Requirement: 隧道 ingress SHALL 由对账统一为网关回源

控制面 SHALL 以「绑定该隧道的本地启用路由」为托管清单，通过以下规则合并到隧道 ingress：托管 hostname 的规则 SHALL 统一指向 `Cloudflare:DataPlaneOrigin`（默认 `http://data-plane:8080`）且不带路径；托管清单中缺失的 hostname SHALL 插入兜底规则之前；指向网关 origin 但已不在托管清单的规则 SHALL 视为陈旧托管规则并移除；其余规则（含用户手写的兜底规则内容）SHALL 原样保留；末尾无 hostname 的兜底规则缺失时 SHALL 追加 `Cloudflare:DefaultCatchAll`（默认 `http_status:404`）。合并无变化时 SHALL 不发起 PUT。

#### Scenario: 绑定路由后对账新增 ingress 规则

- **WHEN** 路由「api」以 `api.example.com` 绑定隧道 T 并启用，执行对账
- **THEN** T 的 ingress SHALL 新增 `api.example.com → http://data-plane:8080`
- **AND** 若原 ingress 无兜底规则，末尾 SHALL 追加 `http_status:404` 兜底

#### Scenario: 用户手写的非托管规则不受影响

- **WHEN** 用户已在 Cloudflare 侧为 `blog.example.com` 配置直连 origin 的规则，且该域名不在托管清单
- **THEN** 对账 SHALL 原样保留该规则

#### Scenario: 解除绑定后移除陈旧托管规则

- **WHEN** 路由「api」改回 `local` 或删除，重新对账 T
- **THEN** T 的 ingress 中指向网关 origin 且 host 为 `api.example.com` 的规则 SHALL 被移除

#### Scenario: 兜底规则已有用户内容

- **WHEN** 隧道 ingress 末尾已存在 `http://fallback.example` 兜底规则
- **THEN** 对账 SHALL 保留该兜底内容，不覆盖为 `http_status:404`

### Requirement: 隧道域名 SHALL 自动创建与清理 CNAME 记录

对账 SHALL 为托管 hostname 在最长后缀匹配的 zone 中创建 CNAME 记录，指向 `<tunnel-id>.cfargotunnel.com` 且 `proxied: true`；记录已存在但内容不是隧道目标时 SHALL 报告冲突而不是覆盖。移除托管规则时 SHALL 仅当 CNAME 内容仍等于隧道目标才删除，否则 SHALL 保留并报告。

#### Scenario: 首次绑定创建 CNAME

- **WHEN** 托管 hostname `api.example.com` 在 `example.com` zone 中尚无 CNAME 记录
- **THEN** 对账 SHALL 创建 `api.example.com → <tunnel-id>.cfargotunnel.com`（proxied）

#### Scenario: CNAME 指向其他目标

- **WHEN** `api.example.com` 已有 CNAME 指向别的地址
- **THEN** 对账 SHALL 不覆盖该记录，并在报告中列出冲突

#### Scenario: 解除绑定后删除 CNAME

- **WHEN** `api.example.com` 的 CNAME 内容等于隧道目标，且该 host 退出托管清单
- **THEN** 对账 SHALL 删除该 CNAME 记录

### Requirement: 路由保存 SHALL 与发布解耦并尽力而为

路由保存到本地数据库 SHALL 始终成功（校验通过时）；对 Cloudflare 的发布 SHALL 为同步尽力而为：成功后返回 `RouteSaveResponse.Publish` 包含结果，失败时把错误/冲突收集进 `Publish` 而非使路由保存失败。隧道详情页 SHALL 提供「重对账」以重试。

#### Scenario: Cloudflare 发布失败不阻断路由保存

- **WHEN** 用户保存绑定隧道的路由，但 Cloudflare API 暂不可用
- **THEN** 路由 SHALL 保存成功，响应 `Publish.Published = false` 并携带错误说明
- **AND** 前端 SHALL 以警告提示用户可稍后重对账

#### Scenario: 发布成功后提示新增记录

- **WHEN** 保存隧道路由且对账创建了新 CNAME
- **THEN** 响应 SHALL 列出 `DnsCreated`，前端 SHALL 成功提示包含新增域名

### Requirement: 隧道流量 SHALL 不会被数据面重定向成 HTTPS 循环

数据面 SHALL 继续通过 X-Forwarded-Proto 识别隧道回源的 HTTPS 请求：cloudflared 回源携带 `X-Forwarded-Proto: https`，HttpsRedirect 策略据此 SHALL 不触发 308 重定向，从而避免隧道流量重定向循环。此行为依赖既有 ForwardedHeaders 顺序，本需求不新增数据面代码。

#### Scenario: 隧道回源请求不重定向

- **WHEN** cloudflared 以 `X-Forwarded-Proto: https` 向数据面回源
- **THEN** 请求 SHALL 被正常代理，不返回 `308`

### Requirement: 接入通道列表 SHALL 汇总本地与隧道

`GET /api/connections` SHALL 返回 `local` 连接与全部 Cloudflare 隧道（含状态），供路由表单选择通道与接入通道页面展示。Cloudflare 不可用时该接口 SHALL 仍返回 `local` 而不是整体失败。

#### Scenario: Cloudflare 不可用时仍可选本地

- **WHEN** Cloudflare 凭据缺失或 API 故障
- **THEN** `GET /api/connections` SHALL 返回 `local` 连接（隧道列表为空或忽略），路由表单 SHALL 仍可保存本地路由
