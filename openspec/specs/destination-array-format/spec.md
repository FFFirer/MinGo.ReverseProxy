# destination-array-format Specification

## Purpose
Define the JSON array format for destinations in API requests and responses, with auto-generated IDs.

## MODIFIED Requirements

### Requirement: destinations 使用数组格式

API 的 destinations 字段 SHALL 使用 JSON 数组格式返回和接收。每个元素包含 id（自动生成）、address、healthy 字段。

**变更说明**: `id` 不再由用户提供，改为后端自动生成。创建新目标时，请求中可以省略 `id`，后端自动分配。

#### Scenario: 获取集群返回数组
- **WHEN** 客户端 GET `/apimanagement/clusters/{id}`
- **THEN** 响应中 destinations 为 JSON 数组 `[{"id":"cluster1-1","address":"http://srv1:8080","healthy":true}]`
- **AND** `id` 格式为 `{clusterId}-{序列号}`

#### Scenario: 创建集群提交不带 id
- **WHEN** 客户端 POST `/apimanagement/clusters` 提交 destinations `[{"address":"http://srv1:8080"}]`
- **THEN** 系统 SHALL 自动为每个 destination 生成 `id`
- **AND** 响应中返回完整的 `id` 字段

#### Scenario: 更新集群保留已有 id
- **WHEN** 客户端 PUT `/apimanagement/clusters/{id}` 提交包含已有 `id` 的 destinations
- **THEN** 系统 SHALL 保留传入了 `id` 的目标的 ID
- **AND** 为新目标（无 `id`）自动生成新 ID

### Requirement: 前端直接使用数组

前端 SHALL 直接使用数组格式的 destinations，不展示 `id` 输入框。

#### Scenario: 表单编辑使用数组
- **WHEN** 用户添加/编辑集群表单
- **THEN** destinations 信号直接存储数组，每行只包含地址
- **AND** 提交时 destinations 为数组格式，`id` 字段由后端自动填充
