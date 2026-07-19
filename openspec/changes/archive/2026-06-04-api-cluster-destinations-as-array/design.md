## Context

当前 ClusterConfig.Destinations 使用 Dictionary，API 序列化为 JSON 对象。数据库实体已使用 List，映射层做 Dictionary↔List 转换。

改为数组后消除转换层，前后端数据结构统一。

## Goals / Non-Goals

**Goals：**
- API 返回和接收数组格式 destinations
- 前后端类型对齐
- 移除前端数组↔字典转换逻辑

**Non-Goals：**
- 不改数据库表结构（实体已是 List）
- 不改路由、证书等其他资源

## Decisions

### 数据格式变更

```json
// Before
{"destinations": {"web-api": {"address":"http://...","healthy":true}}}

// After
{"destinations": [{"id":"web-api","address":"http://...","healthy":true}]}
```

### DestinationConfig 新增 Id 字段

字典的 key 现在显式作为 Id 字段存在 DestinationConfig 中。编辑和保存时直接使用数组元素。

## Risks / Trade-offs

- BREAKING CHANGE：前后端必须同步部署
- 存量数据：字典 key 会映射为 Id 字段，向后兼容
