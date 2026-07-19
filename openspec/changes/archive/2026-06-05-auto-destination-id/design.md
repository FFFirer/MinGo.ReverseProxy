## Context

网关集群管理中，目标（Destination）的 ID 目前由用户在前端手动输入。该 ID 仅作为 YARP `Dictionary<string, DestinationConfig>` 的键和健康状态跟踪的标识，不参与路由决策（路由只引用 ClusterId）。用户需要为目标发明有意义的名称，增加了认知负担，同时需要校验唯一性，编辑时还需按 ID 匹配保留健康状态。

## Goals / Non-Goals

**Goals:**
- 目标 ID 由后端自动生成，格式 `{clusterId}-{序列号}`，序列号集群内单调递增永不重复
- 前端表单只保留地址输入，去掉名称输入
- 地址改变视为新目标，健康状态重置
- 简化 IApiDbService / IApiManagementService 接口（去掉 `destinationId` 参数）
- 后端 `CreateClusterAsync` / `UpdateClusterAsync` 中新增目标时自动分配 ID

**Non-Goals:**
- 不改变 Cluster 本身的 ID 生成方式（仍使用 GUID）
- 不改变 Route 相关逻辑
- 不改变健康检查逻辑
- 不做数据迁移（现有数据不兼容，清空重建）

## Decisions

### Decision 1: ID 格式 `{clusterId}-{seq}`

**方案**: 目标 ID = `{clusterId}-{递增数字}`

**理由**:
- `clusterId` 前缀确保跨集群唯一，调试时可追溯到所属集群
- 数字序列号单调递增，永不重用（即使目标被删除，序列号不重置）
- 可读性好，便于日志排查

**实现方式**: 在 `ApiDbService` 中维护一个集群维度的序号计数器。每次新增目标时查询该集群当前最大的序号后缀，+1 后生成新 ID。

```csharp
// 生成下一个序号
private async Task<int> GetNextDestinationSequenceAsync(string clusterId)
{
    var maxId = await _dbContext.Destinations
        .Where(d => d.ClusterId == clusterId)
        .OrderByDescending(d => d.Id)
        .Select(d => d.Id)
        .FirstOrDefaultAsync() ?? "";

    var maxSeq = 0;
    if (maxId.StartsWith(clusterId + "-"))
    {
        int.TryParse(maxId[(clusterId.Length + 1)..], out maxSeq);
    }
    return maxSeq + 1;
}
```

### Decision 2: AddDestinationAsync 去掉 destinationId 参数

**方案**: 接口签名从 `AddDestinationAsync(clusterId, destinationId, destination)` 简化为 `AddDestinationAsync(clusterId, address)`，ID 由服务层自动生成。

**涉及接口**:
- `IApiDbService.AddDestinationAsync`
- `IApiManagementService.AddDestinationAsync`

### Decision 3: UpdateClusterAsync 内部自动分配 ID

**方案**: 更新集群时，传入的 destinations 数组中如果 `id` 为空或不存在，自动分配新 ID。如果 `id` 存在（来自后端的老记录），保留原 ID。

**注意**: 由于"改地址=换目标"，前端编辑时不再按 ID 匹配保留健康状态。更新时，所有 destination 传 `healthy: true`，后端不会尝试匹配保留老状态。

### Decision 4: 删除 UpdateDestinationAsync

**方案**: 不再提供"更新单个目标"的 API。修改地址 = 删除 + 新增。

**理由**: 用户的需求是"地址就是标识符"。如果允许单独更新地址，语义上等于替换了一个目标，不如强制走删+加流程，避免歧义。

**保留**: `AddDestinationAsync` 和 `RemoveDestinationAsync`。

### Decision 5: 前端表单只保留地址输入

**方案**: 目标行从两个输入框（名称 + 地址）改为一个输入框（地址）。编辑时从后端带回的 `id` 作为隐藏字段传递。

**理由**:
- 去掉认知负担
- 简化表单校验
- 地址就是标识符

### Decision 6: 健康状态不保留

**方案**: 每次更新集群时，所有目标健康状态重置为 `true`。不再尝试按 ID 匹配保留健康状态。

**理由**: "改地址=换目标"，即使地址没改，用户更新集群时目标可能经历了重启或其他变化，从保守角度重置健康状态是安全的。

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| 删除 `UpdateDestinationAsync` 可能影响未来需要单独管理目标的场景 | 保留 `AddDestinationAsync` / `RemoveDestinationAsync` 可以覆盖增删场景；如果后续需要单独更新地址（非替换语义），可以再补 |
| 序列号从数据库查询获取，高并发下（极少场景）可能重复 | 实际场景中目标配置变更频率很低，不是高并发路径。如果需要可加分布式锁 |
| 现有数据库数据不兼容，需要清空 | 标记为 BREAKING，开发环境重新初始化即可 |
