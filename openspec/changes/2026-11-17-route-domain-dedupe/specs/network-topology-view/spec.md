## MODIFIED Requirements

### Requirement: 四层节点与有向边

拓扑图 SHALL 按 接入通道 → 路由 → 集群 → 目标端点 四层排布，边方向即流量方向。

**ADDED**: 绑定外部隧道的启用路由 SHALL 画成两跳：`conn:{tunnel} → conn:local`（`origin`，隧道回源到本地数据面）与 `conn:local → route:{id}`（`ingress`）。所有路由的 `ingress` 边 SHALL 从数据面节点出发，因为转发规则一律装在数据面；SHALL NOT 出现 `conn:{tunnel} → route:{id}` 这种让隧道看起来自己完成了后端寻址的直连边。当通道清单里没有 `local` 节点（老版本数据面）时，SHALL 退回 `conn:{tunnel} → route:{id}` 直连边，避免路由在图上悬空，此为图形降级而非行为改变。

#### Scenario: 完整链路成图

- **WHEN** 路由绑定集群且集群含端点
- **THEN** 生成 `conn:{id} → route:{id} → cluster:{id} → dest:{clusterId}:{destId}` 三类边，端点 ID 按所属集群命名空间隔离

#### Scenario: 多路由共享集群

- **WHEN** 多条路由指向同一集群
- **THEN** 该集群只生成一个节点，并列出引用它的路由

#### Scenario: 引用目标不存在时不造占位节点

- **WHEN** 路由指向的集群或通道已不存在
- **THEN** 不生成占位节点、不生成指向它的边，改由配置体检查报

#### Scenario: 隧道路由经数据面中转

- **WHEN** 启用路由绑定某隧道，且通道列表含 `local`
- **THEN** 生成 `conn:{tunnel} → conn:local` 的 `origin` 边与 `conn:local → route:{id}` 的 `ingress` 边
- **AND** 不生成 `conn:{tunnel} → route:{id}` 直连边

#### Scenario: 同一隧道的多条路由共享中转跳

- **WHEN** 多条路由绑定同一隧道
- **THEN** `conn:{tunnel} → conn:local` 只有一条边，`ingress` 边按路由各自一条

#### Scenario: 缺少数据面节点时退回直连

- **WHEN** 路由绑定隧道但通道列表内没有 `local`
- **THEN** 生成 `conn:{tunnel} → route:{id}` 的 `ingress` 边且不生成 `origin` 边

## ADDED Requirements

### Requirement: 路由节点 SHALL 暴露真实转发路径

路由节点的详情 SHALL 包含「转发路径」一项，写明该条路由的流量实际经过哪些进程：绑定隧道的路由为 `{隧道名} → 本地数据面 → 集群`，本地路由为 `本地数据面 → 集群`。该项 SHALL 与底层规则编译行为一致，用于让运维在图上确认「隧道不直连后端」。

#### Scenario: 隧道路由显示两跳

- **WHEN** 拓扑聚合一条绑定隧道「远端隧道」的路由
- **THEN** 该节点详情的「转发路径」为 `远端隧道 → 本地数据面 → 集群`

#### Scenario: 本地路由显示单跳

- **WHEN** 拓扑聚合 `ConnectionId` 为 `local` 或空值的路由
- **THEN** 该节点详情的「转发路径」为 `本地数据面 → 集群`

### Requirement: 重复匹配体检 SHALL 与保存闸门同口径

`duplicate-match` 体检 SHALL 使用与保存期去重闸门相同的匹配键（`RouteMatchKey`：域名小写去尾点、空值归一 `*`，路径小写、catch-all 写法归一为任意路径），SHALL 只统计启用路由，并 SHALL 在消息中说明底层只生效一条（按路由 ID 字典序取先者）因此实际命中不可预期。

#### Scenario: 归一化后同键的启用路由被判重

- **WHEN** 两条启用路由分别写作 `API.EXAMPLE.COM /{**catch-all}` 与 `api.example.com. `（空路径）
- **THEN** 产生 `warning` 级 `duplicate-match` 问题并着色两条路由，消息点明只生效一条

#### Scenario: 通配与精确域名不算重复

- **WHEN** 启用路由 `*.example.com /v1/*` 与 `api.example.com /v1/*` 并存
- **THEN** 不产生 `duplicate-match` 问题（实测精确域名优先命中，属有意分层）
