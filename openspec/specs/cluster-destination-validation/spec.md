# cluster-destination-validation Specification

## Purpose
TBD - created by archiving change improve-cluster-destinations. Update Purpose after archive.
## Requirements
### Requirement: 禁止重复目标 ID

集群表单提交时，SHALL 校验 destinations 数组中各行的 id 字段是否唯一。

#### Scenario: 重复 ID 提示错误
- **WHEN** 用户提交表单时 destinations 中存在两条或以上具有相同 id 的行
- **THEN** 表单显示错误信息"目标 ID 不能重复"，不提交

### Requirement: 编辑时按索引保留健康状态

编辑已有集群时，destinations 的健康状态 SHALL 按数组索引匹配，而非按 ID 匹配。

#### Scenario: 修改目标名称后健康状态保留
- **WHEN** 用户编辑集群，将某目标的名称从 "web-api" 改为 "api-v2"
- **THEN** 该目标的健康状态保持编辑前的值不变

### Requirement: 数组转字典转换

提交时 destinations 由数组转为字典（Record），转换逻辑 SHALL：
- 过滤掉 id 或 address 为空的行
- 按数组索引匹配原有健康状态

#### Scenario: 新增集群提交转换
- **WHEN** 用户添加新集群并提交
- **THEN** destinations 被转换为键值对字典格式提交

