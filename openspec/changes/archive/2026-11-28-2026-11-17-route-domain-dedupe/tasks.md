## 1. Core：匹配键与规则编译

- [x] 1.1 新增 `src/MinGo.Core/Routing/RouteMatchKey.cs`：`(Host, Path)` 归一化键，域名小写去尾点、空值归一 `*`，catch-all 写法归一为任意路径
- [x] 1.2 新增 `src/MinGo.Core/Routing/DataPlaneRuleCompiler.cs`：过滤停用路由 → 按匹配键去重（同键留 ID 字典序首条）→ 稳定排序，产出 `Rules` / `Suppressed` / `Warnings`
- [x] 1.3 编译器不读 `ConnectionId`，注释写明理由（接入通道只决定流量入口，规则一律装在数据面）

## 2. 控制面：保存期去重闸门

- [x] 2.1 新增 `src/MinGo.ControlPlane.Api/Services/RouteUniquenessPolicy.cs`：候选与表内启用路由比键，命中返回不可保存 + 面向运维的错误文案（点名占用路由、其接入通道、改法）
- [x] 2.2 `ApiManagementController.CreateRoute` 接入闸门，冲突返回 400
- [x] 2.3 `ApiManagementController.UpdateRoute` 接入闸门，排除被覆盖路由自身，冲突返回 400

## 3. 控制面：底层配置生成

- [x] 3.1 `ConfigReplicationService` 抽出 `AddRoutesToSnapshot`，全量快照与增量广播共用
- [x] 3.2 路由来源改为 `DataPlaneRuleCompiler.Compile`，`Suppressed` 逐条记 Warning 日志

## 4. 控制面：拓扑呈现真实链路

- [x] 4.1 `TopologyService` 为隧道路由新增 `conn:{tunnel} → conn:local` origin 边，ingress 边改由数据面出发；数据面节点缺失时退回直连
- [x] 4.2 路由节点详情新增「转发路径」
- [x] 4.3 `duplicate-match` 分组改用 `RouteMatchKey`，文案说明底层只生效一条

## 5. 前端：路由页

- [x] 5.1 `Routes.tsx` 增加与后端同口径的匹配键归一化 helper（注释标注必须同步）
- [x] 5.2 表单冲突实时提示（域名字段下琥珀色）并阻断提交
- [x] 5.3 选择外部隧道时说明将生成的两条底层规则（隧道 ingress + 数据面转发）
- [x] 5.4 列表在隧道路由的接入通道列标注「经数据面转发」

## 6. 测试

- [x] 6.1 `RouteMatchKeyTests`：大小写 / 尾点 / 空白 / catch-all 写法归一，通配与精确不互斥
- [x] 6.2 `DataPlaneRuleCompilerTests`：`Compile_KeepsTunnelBoundRoutes` 契约守卫、输入顺序无关的确定性、同键留一条并告警
- [x] 6.3 `RouteUniquenessPolicyTests`：拦截与放行矩阵（同键、归一化后同键、停用、编辑自身、启用后再改）
- [x] 6.4 `TopologyServiceTests`：origin / ingress 边、共享数据面入边去重、无数据面节点降级、转发路径详情
- [x] 6.5 `YarpMatchPrecedenceTests`：实测锁定 YARP 精确优先与大小写不敏感（归一化口径的依据）
- [x] 6.6 `dotnet test` Core / ControlPlane.Api / DataPlane 全绿；前端 `vite build` 通过

## 7. 遗留

- [ ] 7.1 归档时把 `route-match-uniqueness` 落为正式能力目录，并把改动并入 `cloudflare-tunnel-connections`、`grpc-config-replication`、`network-topology-view` 主规格
- [ ] 7.2 前端归一化与后端漂移的长期解法（由控制面暴露校验接口）暂不做，等出现实际漂移再评估
