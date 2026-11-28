## Why

外部隧道接入模式下，一条绑定隧道的启用路由实际上要在两个地方生效：隧道 ingress（hostname → 本地数据面回源地址）和数据面转发规则（同一条 host/path → 后端集群）。此前前者由 `IngressReconciler` 显式对账，后者只是 `ConfigReplicationService` 内联循环「顺带」产出——没有任何地方写明「隧道不直连后端」这条契约，也没有测试守住它；任何人改成按 `ConnectionId` 过滤下发路由，隧道流量就会落地即 404。

同时，匹配条件完全相同的两条启用路由可以一起保存。它们下发到底层就是两条同键 YARP 规则，命中哪条取决于规则表排列；在隧道 ingress 侧又会合并成一条 hostname 规则，指向哪个集群说不清。控制面只能事后在拓扑页告警，用户保存时毫无感知。

## What Changes

- **新增匹配键 `RouteMatchKey`**：把「域名 + 路径」归一化成一个可比较的键（域名去尾点、转小写，空值归一为 `*`；catch-all 的各种写法归一为「任意路径」）。前端预检、保存闸门、底层编译、拓扑体检统一用它，避免各写一份比较逻辑。
- **新增保存期去重闸门 `RouteUniquenessPolicy`**：候选为启用路由时，与表内其他启用路由比键；命中即 **400 拒绝保存**，错误信息点出占用方、其接入通道和改法。停用路由不占键，也不参与比较。
- **新增 `DataPlaneRuleCompiler`**：底层配置生成从内联循环改为显式编译——只取启用路由、按匹配键确定性排序、同键保留一条（ID 字典序）并对其余产出 Warning。编译刻意不读 `ConnectionId`：接入通道只决定流量从哪进来，规则一律装在数据面。这是「隧道不直连后端」契约的可执行表达。
- **拓扑图补齐真实链路**：隧道路由多一条 `conn:{tunnel} → conn:local` 的 origin 边，ingress 边恒从数据面出发；路由节点详情新增「转发路径」。`duplicate-match` 体检改用同一把匹配键，文案说明底层只生效一条。
- **控制台路由页**：表单实时提示与哪条启用路由冲突并阻断提交；选择外部隧道时说明将生成的两条底层规则；列表在隧道路由上标注「经数据面转发」。

## Capabilities

### New Capabilities

- `route-match-uniqueness`: 路由匹配键的归一化口径，以及保存期「同一域名 + 路径只允许一条启用路由」的闸门行为。

### Modified Capabilities

- `cloudflare-tunnel-connections`: 明确外部隧道不直连后端，绑定隧道的启用路由必须同时产出数据面转发规则。
- `grpc-config-replication`: 下发的路由集合改为经 `DataPlaneRuleCompiler` 编译（停用路由不再进入快照，同键只留一条并记 Warning）。
- `network-topology-view`: 图上画出隧道 → 数据面这一跳；重复匹配判定与保存闸门同口径。

## Impact

- **MinGo.Core**：新增 `Routing/RouteMatchKey.cs`、`Routing/DataPlaneRuleCompiler.cs`（纯函数）。
- **MinGo.ControlPlane.Api**：新增 `Services/RouteUniquenessPolicy.cs`；`ApiManagementController` 的创建/更新接口新增 400 分支；`ConfigReplicationService` 快照路由来源换成编译器；`TopologyService` 的边与详情。
- **frontend/min-go-console**：`pages/Routes.tsx` 增加冲突预检与隧道说明（归一化逻辑与后端同口径，注释已标注必须同步）。
- **兼容性**：无数据库迁移、无 proto 变更。存量重复键的路由仍可读取，但下次编辑保存时会被闸门拦下，需先消除重复。
- **非目标**：不改 `IngressReconciler` 的回源地址对账；不引入按通道过滤下发路由；不动 `DatabaseProxyConfigProvider`（当前无引用的遗留路径）。
