## Context

`ClusterFormModal` 中 destinations 的数据流：

```
初始化（编辑模式）
  props.cluster.destinations: Record<string, DestinationConfig>
  → Object.entries().map() → Array<{id, address}>
  
编辑过程
  Array<{id, address}> → 用户增删改行
  
提交
  Array<{id, address}> → reduce() → Record<string, DestinationConfig>
```

现有问题：
1. 数组转字典时，同 ID 后覆盖前，无提示
2. 编辑时若用户修改了目标名称（id 字段），`healthy` 字段丢失（因为按新 ID 在旧数据中查找不到）

## Goals / Non-Goals

**Goals：**
- 提交时校验 destinations 无重复 ID
- 数组转字典时，同一行继承原有健康状态（按索引匹配而非 ID 匹配）
- 所有改动限在 `Clusters.tsx` 内

**Non-Goals：**
- 不改动 API
- 不改动类型定义
- 不改动其他页面

## Decisions

| 决策 | 选择 | 理由 |
|------|------|------|
| 重复检测时机 | validate() 阶段 | 与现有校验流程一致，统一报错展示 |
| 健康状态匹配策略 | 按数组索引匹配 | 编辑时若用户改了目标名称，按索引能找到对应的原有健康状态；按 ID 匹配会丢失 |
| 空 ID 兜底 | 提交时跳过空 ID 行 | 与现有行为保持一致，空行视为无效 |

## Risks / Trade-offs

- 低风险：仅表单校验层增加逻辑，不改后端
- 编辑时按索引匹配健康状态：如果后端有异步健康检查更新，编辑期间可能会拿到过时的健康状态。但当前编辑时仅读取 `props.cluster.destinations` 的快照，不存在竞态问题
