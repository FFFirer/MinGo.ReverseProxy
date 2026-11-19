## 1. Spike：实测锁定 YARP 健康检查 API

- [ ] 1.1 新增 `tests/MinGo.DataPlane.Tests/YarpHealthCheckSpikeTests.cs`（HostBuilder + Kestrel + `LoadFromMemory` + mock 后端）：主动检查开箱可用、探测路径/间隔生效、后端关停后状态翻转
- [ ] 1.2 同一 spike 锁定 keyed `IClusterDestinationsState` 解析方式、`DestinationHealthState` 枚举取值、`Response.ExpectedStatusCodes` 空列表语义、被动策略名校验行为
- [ ] 1.3 实测结论与 design.md 决策表不符时，先改 design.md 再继续

## 2. Proto：心跳追加目标健康段

- [ ] 2.1 `dataplane.proto` 的 `HeartbeatRequest` 追加 `repeated DestinationHealth destination_health = 10`（`cluster_id=1, destination_id=2, is_healthy=3`），注释写明追加约定

## 3. 数据面：翻译与上报

- [ ] 3.1 `TranslateConfig` 翻译 `health_check_json` → YARP `HealthCheckConfig`（主动/被动/期望状态码），`dest.Healthy` → `DestinationConfig.Health`；被动策略白名单，非法值回退默认并记 Warning
- [ ] 3.2 新增状态读取接口（如 `IDestinationHealthReader`）：从 keyed `IClusterDestinationsState` 读每端点折叠布尔，默认实现放 `DataPlaneConfigProvider` 或独立小类
- [ ] 3.3 `GetConfigSnapshotJson` 的 `Healthy = true` 硬编码改为读运行时状态
- [ ] 3.4 `HeartbeatRequestBuilder` 追加 `destination_health` 段（经 `IDestinationHealthReader`，可 mock）
- [ ] 3.5 `Program.cs` 注册新依赖（如需）

## 4. 控制面：聚合与落库

- [ ] 4.1 新增 `Services/DestinationHealthAggregator.cs` 单例：per-数据面条目 + fail-closed 聚合，仅聚合值变化时产出待写列表
- [ ] 4.2 `ApiDbService` 新增 `UpdateDestinationHealthAsync(clusterId, destinationId, healthy)`：仅改 `Healthy/UpdatedAt`，端点不存在返回 false
- [ ] 4.3 `HeartbeatCollectService` 接入聚合器，处理 `destination_health`（空列表=老数据面，跳过）
- [ ] 4.4 数据面断连时在现有 `Disconnect` 路径清理该实例条目

## 5. 控制面：拓扑文案切换运行态

- [ ] 5.1 `TopologyService` 端点文案"配置健康/配置不健康"→"运行健康/运行不健康"
- [ ] 5.2 集群节点"X 端点 · Y 健康"与体检项口径不变（数据源已变真实），注释同步

## 6. 前端

- [ ] 6.1 `Clusters.tsx` 被动策略下拉改为 `FailureRate`/`TransportFailureRate`（与 YARP 合法值对齐）
- [ ] 6.2 `Topology.tsx` 图注改为"状态来自数据面探测并经心跳回传（心跳周期级延迟）"

## 7. 测试与验收

- [ ] 7.1 `TranslateConfig` 健康检查映射单测：主动/被动/期望状态码/非法策略回退/停用检查
- [ ] 7.2 `HeartbeatRequestBuilder` 折叠逻辑单测（mock `IDestinationHealthReader`）
- [ ] 7.3 `DestinationHealthAggregator` 单测：多实例聚合、翻转才写库、断连清理、不存在端点忽略
- [ ] 7.4 YARP E2E：短间隔主动检查 + 后端关停 → 状态翻转 → 心跳携带 `false`（轮询断言，容忍探测延迟）
- [ ] 7.5 `TopologyServiceTests` 文案断言更新
- [ ] 7.6 `dotnet test` Core / ControlPlane.Api / DataPlane 全绿；前端 `vite build` 通过

## 8. 遗留

- [ ] 8.1 归档时把 `destination-health` 落为正式能力目录，并同步并入四个受影响能力主规格
- [ ] 8.2 存量库中被动策略为 `ConsecutiveFailures` 的集群，数据面按回退口径运行（记 Warning），UI 打开编辑即见新选项；不做数据迁移
