## MODIFIED Requirements

### Requirement: 路由 SHALL 通过 ConnectionId 选择接入通道

`RouteConfig.ConnectionId` SHALL 取值为 `local`（内置数据面，`ConnectionIds.Local` 常量）或某个 Cloudflare 隧道 ID。空值或空白 SHALL 视为 `local`。新增路由默认 SHALL 为 `local`，现有路由在升级迁移后 SHALL 保持 `local`，数据面（YARP）SHALL 忽略该字段、继续处理本地流量。

**ADDED**: `ConnectionId` SHALL 只决定流量从哪里进入，不决定转发规则装在哪个进程。绑定隧道的启用路由 SHALL 同时在隧道 ingress（hostname → 本地数据面回源地址）与数据面转发规则（同一匹配条件 → 后端集群）两处生效；数据面生成规则时 SHALL NOT 按 `ConnectionId` 过滤路由。

#### Scenario: 新建路由默认走本地数据面

- **WHEN** 用户在路由表单中不修改接入通道
- **THEN** 保存的 `ConnectionId` 为 `local`，路由照常发布到数据面

#### Scenario: 历史路由升级后仍走本地

- **WHEN** 数据库升级应用 `AddRouteConnectionId` 迁移
- **THEN** 已有路由的 `ConnectionId` SHALL 为 `local`，行为与升级前一致

#### Scenario: 绑定隧道的启用路由同时产出数据面规则

- **WHEN** 一条启用路由的 `ConnectionId` 为某个隧道 ID
- **THEN** 该隧道 ingress 里存在指向本地数据面回源地址的 hostname 规则
- **AND** 下发给数据面的配置里存在同匹配条件、指向该路由集群的转发规则

#### Scenario: 隧道流量经数据面而非直连后端

- **WHEN** 请求经隧道 ingress 回源到本地数据面
- **THEN** 数据面按自身的同条件规则转发到后端集群，SHALL NOT 出现「隧道直连后端」的配置形态（回源地址始终指向数据面）

## ADDED Requirements

### Requirement: 隧道接入模式 SHALL 在控制台说明其转发链路

选择外部隧道作为接入通道时，路由表单 SHALL 说明该路由将生成的两条底层规则（隧道 ingress 回源到数据面、数据面转发到目标集群），并在路由列表中标注这类路由「经数据面转发」。说明文案 SHALL 与实际编译行为一致，避免运维误以为隧道自己完成了后端寻址。

#### Scenario: 表单展示隧道链路说明

- **WHEN** 用户在路由表单把接入通道切到某个隧道
- **THEN** 表单显示该路由会同时生成隧道 ingress 回源规则与数据面转发规则，并点明下一跳是本地数据面

#### Scenario: 列表标注中转语义

- **WHEN** 路由列表渲染一条绑定隧道的路由
- **THEN** 接入通道列在该路由上标注「经数据面转发」
