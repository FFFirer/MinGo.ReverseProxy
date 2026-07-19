# cluster-destination-validation Specification

## Purpose
Define validation rules for cluster destinations. With auto-generated IDs, duplicate ID checks and health state preservation by ID matching are no longer needed.

## REMOVED Requirements

### Requirement: 禁止重复目标 ID

**Reason**: ID 已改为后端自动生成，不存在用户输入重复的问题。

**Migration**: 不再需要前端校验 ID 唯一性。ID 由后端保证唯一。

### Requirement: 编辑时按索引保留健康状态

**Reason**: "地址即标识符"——地址改变视为新目标，不再需要按 ID 匹配保留健康状态。所有目标在更新时健康状态重置为 `true`。

**Migration**: 更新集群时 health 统一设为 `true`，不再按索引匹配。

## ADDED Requirements

### Requirement: 地址非空校验

提交时 SHALL 校验 destination 的 address 字段不能为空。

#### Scenario: 空地址提示错误
- **WHEN** 用户提交表单时存在 address 为空的行
- **THEN** 表单提示"目标地址不能为空"，不提交

### Requirement: 数组转字典转换

提交时 destinations 由数组转为字典（Record），转换逻辑 SHALL：
- 过滤掉 address 为空的行
- 保留已有的 id（如果有），新行不传 id

#### Scenario: 新增集群提交转换
- **WHEN** 用户添加新集群并提交
- **THEN** destinations 被转换为键值对字典格式提交，id 由后端自动生成
