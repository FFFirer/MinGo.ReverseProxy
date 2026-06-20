## Why

配置从 Control Plane 推送到 Data Plane 时没有任何校验。如果数据库中存在无效配置（如 Route 引用不存在的 Cluster、Destination 地址格式错误、无效的负载均衡策略等），这些配置会被直接推送到所有 Data Plane，导致路由失败、YARP 启动异常或运行时错误。

需要在推送前校验配置合法性，拒绝无效配置，保证线上配置始终有效。

## What Changes

- **新增 ConfigValidator**：在 MinGo.Core 中实现配置校验器，校验 ConfigSnapshot 的合法性
- **新增校验结果模型**：ValidationResult、ConfigValidationError、ConfigValidationWarning
- **集成到 ConfigReplicationService**：在 BuildConfigSnapshotAsync 和 BroadcastConfigUpdateAsync 中调用校验器
- **校验失败处理**：记录错误日志，拒绝推送，Data Plane 继续使用旧配置
- **校验规则覆盖**：
  - Route → Cluster 引用完整性
  - Destination Address URL 格式
  - LoadBalancingPolicy 合法性
  - TransformsJson 格式
  - 证书数据有效性和过期检查

## Capabilities

### New Capabilities

- `config-validation`: 配置推送前校验能力，确保 ConfigSnapshot 中的所有 Route、Cluster、Destination、Certificate 数据合法

### Modified Capabilities

- `grpc-config-replication`: 校验失败时拒绝推送配置到 Data Plane（现有行为不变，仅增加校验拦截）

## Impact

- **MinGo.Core**：新增 Validation/ 目录和相关类
- **MinGo.ControlPlane.Api**：ConfigReplicationService 集成 ConfigValidator
- **MinGo.DataPlane**：无直接修改（Data Plane 信任 Control Plane 的校验结果）
- **现有配置**：数据库中的无效配置将被拦截，不会推送到 Data Plane
