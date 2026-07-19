## Context

当前实例管理存在三层断层：

```
数据面 (gRPC)         控制面                         前端
──────────         ──────────────────           ────────
HeartbeatReporter → HeartbeatCollectService
                    → DataPlaneConnectionManager (仅内存)
                                               Instances.tsx
ConfigReplication → ConfigReplicationService    → GET /api/instances
                    → DataPlaneConnectionManager  → GatewayInstanceService (空桩)
                                                    → 永远返回空
```

- `GatewayInstanceService` 的 7 个方法全是空桩，不存储任何数据
- `HeartbeatCollectService` 收到心跳后只更新 `DataPlaneConnectionManager` 的 `LastHeartbeat`，不写回实例服务
- `ConfigReplicationService` 处理数据面连接/断开，但不注册/注销实例
- `DataPlaneConnectionManager` 和 `GatewayInstanceService` 是两个独立的内存状态，没有关联

## Goals / Non-Goals

**Goals:**
- GatewayInstanceService 用 `ConcurrentDictionary<string, GatewayInstance>` 实现完整的内存存储
- gRPC 心跳实时更新实例指标 (CPU/内存/请求数)
- 数据面连接/断开自动注册/注销实例
- 前端展示实时实例列表和指标
- 30 秒无心跳自动标记 HeartbeatTimeout

**Non-Goals:**
- 不引入数据库 (EF Core / SQL)
- 不修改 gRPC proto
- 不修改 REST API 端点签名 (InstancesController 保持不变)
- 不做实例详情页 (未来迭代)
- 不做 WebSocket/SSE 实时推送 (页面刷新获取最新)

## Decisions

### 1. GatewayInstanceService 注册策略：Singleton + ConcurrentDictionary

**选择**：`AddSingleton<IGatewayInstanceService, GatewayInstanceService>()`

**理由**：实例数据必须跨请求、跨连接共享。Scoped 会在每个 HTTP 请求创建新实例，导致数据丢失。

**替代方案**：引入 Redis/内存缓存 → 当前不需要，ConcurrentDictionary 足够。

### 2. 实例自动注册策略：在 ConfigReplicationService 连接时触发

```
ConfigReplication (gRPC connect)
  → ConfigReplicationService.ReplicateConfig()
    → register into GatewayInstanceService
    → stream stays open
  → on disconnect/cancel
    → mark as Offline
```

**数据面 ID**：使用 `ConfigSubscription.data_plane_id`（8 位 hex 字符串）作为 `InstanceId`。名称默认为 `"DataPlane-{shortId}"`。

**地址信息**：当前 proto 的 ConfigSubscription 不包含监听地址，留空。后续如需展示可通过扩展 proto 补充。

### 3. 心跳联动策略

```
HeartbeatRequest (gRPC stream)
  → HeartbeatCollectService.ReportHeartbeat()
    → update DataPlaneConnectionManager.LastHeartbeat (保持兼容)
    → update GatewayInstanceService (新增)
```

两处都更新，保持向后兼容。如果未来废弃 DataPlaneConnectionManager，可平滑迁移。

### 4. 超时检测策略

不在后台启动 Timer，而是在 `GetInstancesAsync()` 被调用时惰性检测。

```
每次 GetInstancesAsync() 被调用:
  foreach instance:
    if Status == Online && (UtcNow - LastHeartbeat) > 30s → HeartbeatTimeout
    if Status == HeartbeatTimeout && (UtcNow - LastHeartbeat) > 120s → Offline
```

**理由**：简化实现，避免额外后台服务。数据面心跳每 10 秒一次，30 秒超时窗口足够。

## Risks / Trade-offs

- **[风险] 实例在不更新心跳时无法自动发现**：只有通过 gRPC 连接的数据面才能被注册。非 gRPC 场景需通过 REST API 手动注册 → 当前仅 gRPC 场景，可接受
- **[风险] 实例 ID 为运行时随机生成**：数据面每次重启生成新 ID，控制面无法区分同一物理实例的重启 → 当前"仅展示"场景可接受
- **[风险] 内存泄漏**：极端情况下大量实例频繁连接/断开会导致 dictionary 堆积 → 通过 Offline 实例惰性清理 (`CleanupLongTimeTimeoutInstancesAsync`) 缓解
