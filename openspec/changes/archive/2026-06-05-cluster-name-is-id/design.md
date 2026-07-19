## Context

当前 `ApiClusterEntity` 和 `ClusterConfig` 同时有 `Id`（后端自动 GUID）和 `Name`（用户输入）两个字段。路由通过 `ClusterId` 引用集群，存储的是 GUID。目标 ID 格式为 `{GUID}-{seq}`。

这样就产生了三个问题：
1. 用户看到的集群名称（Name）和系统内部标识（GUID）是两套东西，日志排查需要映射
2. 路由配置中 `clusterId: "a1b2c3d4-..."` 没有任何可读性
3. 目标 ID `a1b2c3d4-...-1` 同样不可读

## Goals / Non-Goals

**Goals:**
- 集群 ID 由用户指定（即集群名称），不再自动生成 GUID
- 移除 `Name` 字段，消除 Id/Name 双重标识
- 路由 ClusterId 存储人类可读的集群名称
- 目标 ID 自动变为 `{clusterName}-{seq}`
- 集群名称不可修改（rename = delete + recreate）
- 不兼容现有数据

**Non-Goals:**
- 不改动 Route 相关字段（RouteConfig.ClusterId 字段名不变，只是值的语义变化）
- 不改动负载均衡、健康检查等逻辑
- 不做数据迁移

## Decisions

### Decision 1: Id 由用户传入，后端不再生成 GUID

`CreateClusterAsync` 中：
```csharp
// 之前
entity.Id = Guid.NewGuid().ToString();

// 之后
entity.Id = cluster.Id;  // 用户传入的名称，如 "prod-api"
```

需要增加校验：集群名称非空，且在数据库中唯一。

### Decision 2: 不提供改名功能

和 destination "地址即标识符"一致——集群名称是永久标识。如果用户想改名，需要删除集群重建（同时重建路由引用）。

**理由**：
- 避免级联更新路由的 ClusterId 引用
- 与姐妹 change `auto-destination-id` 的设计哲学一致

### Decision 3: API 设计不变，只是数据含义变化

现有的 `POST /api/apimanagement/clusters` 和 `PUT /api/apimanagement/clusters/{id}` 接口不变。`ClusterConfig` 移除了 `Name` 字段后，请求体更简洁：

```json
// 之前
{ "id": "", "name": "prod-api", "destinations": [...] }

// 之后
{ "id": "prod-api", "destinations": [...] }
```

### Decision 4: 前端改动

- **集群表单**: 去掉"集群名称"标签，改为"集群 ID/名称"提示。输入的值直接赋给 `cluster.id`
- **集群卡片**: 标题直接显示 `cluster.id`
- **路由表单**: 下拉选项的 label 从 `c.name || c.id` 改为 `c.id`
- **类型定义**: 移除 `ClusterConfig.name` 字段

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| 用户想给集群起一个友好名称但希望 ID 用简写 | 目前没有这种需求。如果后续需要 displayName，可以加一个可选字段 |
| 集群名称冲突（两个用户想建同名的集群） | 由数据库唯一索引保证，这是期望行为——名称就应该是唯一的 |
| 现有数据不兼容 | BREAKING 标记，开发环境清空重来即可 |
