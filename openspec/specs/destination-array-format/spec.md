# destination-array-format Specification

## Purpose
TBD - created by archiving change api-cluster-destinations-as-array. Update Purpose after archive.
## Requirements
### Requirement: destinations 使用数组格式

API 的 destinations 字段 SHALL 使用 JSON 数组格式返回和接收，每个元素包含 id、address、healthy 字段。

#### Scenario: 获取集群返回数组
- **WHEN** 客户端 GET `/apimanagement/clusters/{id}`
- **THEN** 响应中 destinations 为 JSON 数组 `[{"id":"...","address":"...","healthy":true}]`

#### Scenario: 提交集群使用数组
- **WHEN** 客户端 PUT/POST `/apimanagement/clusters`
- **THEN** 请求体 destinations 为 JSON 数组格式

### Requirement: 前端直接使用数组

前端 SHALL 直接使用数组格式的 destinations，移除 Object.entries() 转换逻辑。

#### Scenario: 表单编辑直接使用数组
- **WHEN** 用户添加/编辑集群表单
- **THEN** destinations 信号直接存储数组，提交时不做字典转换

