# Network Topology View

## Purpose

控制面提供「网络拓扑」视图，把接入通道 → 路由 → 集群 → 目标端点四层配置串成一张有向图，并对配置一致性做体检，让运维在改配置前后能立刻看到"这条链路通不通、哪里会失败"。拓扑是只读视图，不提供任何写操作。

## Requirements

### Requirement: 拓扑聚合端点

控制面 SHALL 提供 `GET /api/topology` 一次性返回图数据与体检结果，前端不自行拼装多个接口。

#### Scenario: 已登录用户获取拓扑

- **WHEN** 已认证用户请求 `GET /api/topology`
- **THEN** 返回 `TopologyResponse`，含 `generatedAt`、`summary`、`nodes`、`edges`、`issues`

#### Scenario: 未认证请求被拒

- **WHEN** 未登录用户请求 `GET /api/topology`
- **THEN** 返回 401，不泄露配置内容

#### Scenario: 状态以字符串而非枚举传输

- **WHEN** 序列化节点状态、边类型或问题严重度
- **THEN** 取值为字符串常量（`ok`/`warning`/`danger`/`disabled`、`info`/`warning`/`danger`），因为控制面未启用枚举字符串化

### Requirement: 四层节点与有向边

拓扑图 SHALL 按 接入通道 → 路由 → 集群 → 目标端点 四层排布，边方向即流量方向。

#### Scenario: 完整链路成图

- **WHEN** 路由绑定集群且集群含端点
- **THEN** 生成 `conn:{id} → route:{id} → cluster:{id} → dest:{clusterId}:{destId}` 三类边，端点 ID 按所属集群命名空间隔离

#### Scenario: 多路由共享集群

- **WHEN** 多条路由指向同一集群
- **THEN** 该集群只生成一个节点，并列出引用它的路由

#### Scenario: 引用目标不存在时不造占位节点

- **WHEN** 路由指向的集群或通道已不存在
- **THEN** 不生成占位节点、不生成指向它的边，改由配置体检查报

### Requirement: 配置一致性体检

拓扑服务 SHALL 在返回图数据的同时计算配置问题，并按严重度分级。

#### Scenario: 路由指向缺失集群

- **WHEN** 路由的 `clusterId` 在集群集合中不存在
- **THEN** 产生 `danger` 级 `orphan-route` 问题并着色该路由节点

#### Scenario: 路由绑定已删除的隧道

- **WHEN** 路由 `connectionId` 指向的接入通道已不在通道列表内
- **THEN** 产生 `danger` 级 `connection-missing` 问题，且只影响该条路由

#### Scenario: 启用路由匹配重复

- **WHEN** 两条启用路由归一化后的 Host 与 Path 相同
- **THEN** 产生 `warning` 级 `duplicate-match` 问题并着色相关路由；停用路由不参与重复判定

#### Scenario: 集群无端点或端点不健康

- **WHEN** 集群没有任何目标端点
- **THEN** 产生 `warning` 级 `empty-cluster` 问题
- **WHEN** 集群端点全部不健康且存在启用路由引用
- **THEN** 产生 `danger` 级 `all-destinations-unhealthy` 问题
- **WHEN** 仅部分端点不健康
- **THEN** 产生 `warning` 级问题；若引用方全部停用则降级为 `info`

#### Scenario: 未被引用的集群与隧道状态

- **WHEN** 集群没有任何路由引用
- **THEN** 产生 `info` 级 `unused-cluster` 问题，节点状态保持 `ok`
- **WHEN** Cloudflare 隧道状态为 `down`
- **THEN** 通道节点报 `danger`；状态为 `degraded`/`inactive`/未知时报 `warning`

#### Scenario: `info` 不改变节点配色

- **WHEN** 某节点只关联 `info` 级问题
- **THEN** 节点状态仍为 `ok`，`info` 仅出现在体检列表中

### Requirement: 端点健康为配置态

拓扑图中的端点健康状态 SHALL 表示控制面落库的配置值，不表示实时探测结果。

#### Scenario: 展示配置健康标记

- **WHEN** 渲染目标端点节点
- **THEN** 圆点取自 `DestinationConfig.Healthy`，详情字段标注为"配置健康"

#### Scenario: 页面说明实时健康差异

- **WHEN** 用户查看拓扑页面
- **THEN** 页面提示实时探测结果需到实例详情按实例查看，因为 `ApiDbService` 落库时恒将 `Healthy` 置为 true

### Requirement: 拓扑页面交互

前端 SHALL 提供独立「网络拓扑」页面，侧边栏入口位于集群管理之后。

#### Scenario: 自适应缩放与手动缩放

- **WHEN** 首次加载或容器宽度变化且未手动缩放
- **THEN** 图按可用宽度自适应；用户手动缩放后保持其选择

#### Scenario: 悬停与选中高亮整条链路

- **WHEN** 用户悬停或点击某节点
- **THEN** 沿上下游双向闭包高亮相关节点与边，其余元素淡出

#### Scenario: 搜索与只看异常

- **WHEN** 用户输入关键词或勾选"只看异常"
- **THEN** 图按节点名称/域名/路径过滤，或只保留异常节点所属的完整链路，并显示筛选后的节点数

#### Scenario: 节点详情与跳转

- **WHEN** 用户选中节点
- **THEN** 右侧面板按顺序展示该节点的键值详情与关联问题，并提供跳转到对应管理页的入口

#### Scenario: 动态类名规避 Tailwind v4 限制

- **WHEN** 按状态切换节点配色或透明度
- **THEN** 使用字面量类名映射表，不使用运行时拼接的类名
