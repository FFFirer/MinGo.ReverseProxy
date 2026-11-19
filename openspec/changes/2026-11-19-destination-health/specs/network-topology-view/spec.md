## MODIFIED Requirements

### Requirement: 端点健康为运行态

拓扑图中的端点健康状态 SHALL 表示数据面探测并经心跳回传的控制面落库值（运行态），延迟为心跳周期级。SHALL NOT 再以"配置标记"口径展示。

#### Scenario: 展示运行健康状态

- **WHEN** 渲染目标端点节点
- **THEN** 圆点取自 `DestinationConfig.Healthy`（由心跳回传更新），详情字段标注为"运行健康"

#### Scenario: 页面说明状态来源与延迟

- **WHEN** 用户查看拓扑页面
- **THEN** 页面提示端点状态来自数据面健康检查并经心跳回传，延迟为心跳周期级（约 10 秒 + 探测间隔）

### Requirement: 配置一致性体检

拓扑服务 SHALL 在返回图数据的同时计算配置问题，并按严重度分级。端点不健康相关体检项 SHALL 基于运行态落库值判定。

#### Scenario: 集群无端点或端点不健康

- **WHEN** 集群没有任何目标端点
- **THEN** 产生 `warning` 级 `empty-cluster` 问题
- **WHEN** 集群端点全部不健康且存在启用路由引用
- **THEN** 产生 `danger` 级 `all-destinations-unhealthy` 问题
- **WHEN** 仅部分端点不健康
- **THEN** 产生 `warning` 级问题；若引用方全部停用则降级为 `info`

（判定口径不变，数据源由"配置标记"切换为运行态落库值。）
