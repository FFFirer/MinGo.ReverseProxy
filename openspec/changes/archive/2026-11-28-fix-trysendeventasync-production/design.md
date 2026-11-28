## Context

当前 `Program.cs` 第 128-132 行将 `InstanceConfigQueryService.TrySendEventAsync` 回调的装配放在了 `if (app.Environment.IsDevelopment())` 块内。这意味着生产环境中该回调为 null，`GET /api/instances/{id}/config` 无法使用 EventSubscription gRPC 通道向数据面发送 CONFIG_QUERY 事件。

```csharp
// 当前代码（Program.cs L117-L133）
if (app.Environment.IsDevelopment())
{
    // DB 迁移和种子数据（应保留在 Development 块内）
    using (var scope = app.Services.CreateScope()) { ... }

    // ❌ 回调装配不应被 Development 条件守卫
    configQueryService.TrySendEventAsync = (instanceId, eventMsg) =>
        eventSubService.TrySendEventAsync(instanceId, eventMsg);
}
```

## Goals / Non-Goals

**Goals:**
- 生产环境中 `TrySendEventAsync` 回调被正确装配，配置查询 API 可用
- 改动最小，不改变任何业务逻辑

**Non-Goals:**
- 不涉及数据面逻辑修改
- 不涉及 API 签名或行为变更

## Decisions

**Decision 1：将回调装配从 Development 块移至无条件区域**

将 `TrySendEventAsync` 的赋值代码移出 `if (app.Environment.IsDevelopment())` 块，放在 `if` 块之后、`app.UseRouting()` 之前。

```csharp
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope()) { ... }  // 仅迁移+种子数据
}

// ✅ 无条件执行
var configQueryService = app.Services.GetRequiredService<InstanceConfigQueryService>();
var eventSubService = app.Services.GetRequiredService<EventSubscriptionService>();
configQueryService.TrySendEventAsync = (instanceId, eventMsg) =>
    eventSubService.TrySendEventAsync(instanceId, eventMsg);
```

**理由**：
- `InstanceConfigQueryService` 和 `EventSubscriptionService` 都已注册为 Singleton 服务，在 Build() 之后随时可解析
- 回调装配是必要的依赖注入后处理，与开发/生产环境无关
- 改动仅涉及代码位置移动，无新增依赖或逻辑变更

## Risks / Trade-offs

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 无。改动为纯代码块范围调整，不涉及逻辑变更 | — | 部署后通过 `GET /api/instances/{id}/config` 验证 |
