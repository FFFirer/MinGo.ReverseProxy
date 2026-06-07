## Context

当前架构中，控制面（ControlPlane.Api）通过 gRPC 双向流将配置推送到数据面（DataPlane）实例。配置路径：

```
API 用户操作 → ApiManagementService → SQLite 持久化
                                     → ConfigUpdateGrpcBroadcaster
                                     → ConfigReplicationService.BroadcastConfigUpdateAsync()
                                     → gRPC ConfigSnapshot 双向流 → DataPlaneConfigProvider.ApplyConfig()
```

这条路径存在一个问题：**控制面只知道"推送了什么"，不知道数据面"实际在用什么"**。gRPC 推送可能失败、数据面可能重启后配置未同步、配置转换可能存在差异。现状下没有任何方式可以验证数据面的实际运行时配置。

已废弃的 `MinGo.Gateway` 项目中曾有一个 `ConfigController.GetCurrentConfig()` 端点通过 HTTP 直接读取 `DatabaseProxyConfigProvider.GetConfig()`，但那是在同一进程内且需要额外的 HTTP 端口和认证。

数据面（DataPlane）当前只暴露代理端口 `:8080`，没有管理端口。增加 HTTP 管理端点会：
- 增加攻击面（管理流量和代理流量混合）
- 需要认证机制
- 违反职责分离

**设计选择**：复用已有的 gRPC `EventSubscription` 双向通道。每个数据面实例在连接时已经建立了三条 gRPC 流（ConfigReplication、HeartbeatCollect、EventSubscription），其中 EventSubscription 是纯双向的事件通道，适合做查询-响应模式。

## Goals / Non-Goals

**Goals:**
- 控制面能通过 API 查询指定数据面实例的当前 YARP 运行时配置
- 复用已有 gRPC EventSubscription 通道，不新增数据面端口
- 前端支持查看实例配置（Routes + Clusters 结构化展示）
- 支持超时和错误处理（实例离线、超时未响应）

**Non-Goals:**
- 不修改配置推送机制（ConfigReplication 不变）
- 不提供数据面配置的热修改（查询只读）
- 不提供全量实例配置的批量查询（每次查一个实例）
- 不以原始 JSON 展示配置——前端需结构化友好展示

## Decisions

### Decision 1: 通过 EventSubscription 双向通道实现查询

**选择**：在已有 EventSubscription 的 `EventType` 中新增 `CONFIG_QUERY = 7` 和 `CONFIG_REPORT = 8`。

**理由**：
- EventSubscription 已经是双向流，控制面和数据面都可以主动发送事件
- 新的心跳流（HeartbeatCollect）也有指令下发能力，但那是单向的（服务端→客户端指令），不适合请求-响应模式
- ConfigReplication 是服务端推送流，语义是"配置推送"而非"配置查询"
- proto 不需要增加新的 service/rpc，只需增加枚举值

**EventSubscription 现有流程**：
```
控制面 ──EventMessage──→ 数据面 (下行)
数据面 ──EventMessage──→ 控制面 (上行)
CONFIG_QUERY 利用下行通道，CONFIG_REPORT 利用上行通道
```

### Decision 2: 控制面侧使用等待-响应模式

**选择**：控制面维护一个 `ConcurrentDictionary<string, TaskCompletionSource<ConfigSnapshot>>`，发送 CONFIG_QUERY 时创建 TCS，收到 CONFIG_REPORT 时通过 event_id 匹配并完成 TCS。

**理由**：
- EventSubscription 是异步的，需要一种方式将查询请求和响应关联起来
- TCS 是 .NET 中标准的异步等待模式，简单可靠
- 10 秒超时通过 `Task.WhenAny(queryTask, Task.Delay(10000))` 实现
- 不需要 SignalR 或额外的消息队列

### Decision 3: 数据面响应从 DataPlaneConfigProvider 读取实时配置

**选择**：处理 CONFIG_QUERY 时直接调用 `DataPlaneConfigProvider.GetConfig()`。

**理由**：
- `DataPlaneConfigProvider` 实现 `IProxyConfigProvider`，`GetConfig()` 返回的是 YARP 当前正在使用的 `IProxyConfig`
- 配置在 `ApplyConfig()` 时通过 `volatile` 字段原子替换，读取无需加锁
- 返回的 Routes 和 Clusters 是 YARP 的标准类型，直接序列化即可

### Decision 4: 前端配置展示采用结构化面板而非 JSON 查看器

**选择**：使用 Routes 表格面板 + Clusters 卡片面板展示，不提供原始 JSON 视图。

**理由**：
- 用户关心的是"当前有哪些路由规则、指向哪些集群"，不是原始的 YARP 配置结构
- Routes 适合表格（每行一条路由，列：ID、目标集群、路径、域名、状态）
- Clusters 适合卡片列表（每个集群一张卡片，展示策略、目标地址和健康状态）
- 负载均衡策略做中文映射（RoundRobin → 轮询、LeastRequests → 最少连接、Random → 随机），降低认知成本
- 遵循 button-visibility spec，操作列按钮使用 `.btn-text-primary` 文字样式

### Decision 5: 不需要在 proto 中定义新的消息类型

**选择**：复用 `EventMessage`，将配置数据序列化为 JSON 放入 `data_json` 字段。

**理由**：
- 配置查询是管理操作，不需要极致性能
- `EventMessage.data_json` 已经是 `string` 类型，适合承载 JSON
- 避免 proto 文件频繁变更
- 如果将来需要强类型，可以再添加 protobuf 序列化

## 通信流程

```
┌──────────────┐          ┌───────────────┐          ┌──────────────┐
│  Frontend    │          │ ControlPlane  │          │  DataPlane   │
│  (SolidJS)   │          │     .Api      │          │              │
└──────┬───────┘          └───────┬───────┘          └──────┬───────┘
       │                         │                          │
       │ GET /api/instances/     │                          │
       │   dp-abc123/config      │                          │
       │────────────────────────►│                          │
       │                         │                          │
       │                    ┌────┴────┐                     │
       │                    │ 查找实例 │                     │
       │                    │ 验证在线 │                     │
       │                    └────┬────┘                     │
       │                         │                          │
       │                         │ EventMessage {           │
       │                         │   type: CONFIG_QUERY      │
       │                         │   event_id: "q-001"      │
       │                         │ }                        │
       │                         │─────────────────────────►│
       │                         │                          │
       │                         │                    ┌─────┴──────┐
       │                         │                    │ 读取配置    │
       │                         │                    │ GetConfig() │
       │                         │                    └─────┬──────┘
       │                         │                          │
       │                         │ EventMessage {           │
       │                         │   type: CONFIG_REPORT    │
       │                         │   event_id: "q-001"     │
       │                         │   data_json: {           │
       │                         │     version: 1234567890, │
       │                         │     routes: [...],       │
       │                         │     clusters: [...]      │
       │                         │   }                      │
       │                         │ }                        │
       │                         │◄─────────────────────────│
       │                         │                          │
       │                    ┌────┴────┐                     │
       │                    │ 匹配 TCS │                     │
       │                    │ 完成等待 │                     │
       │                    └────┬────┘                     │
       │                         │                          │
       │ 200 OK { routes, clusters }                        │
       │◄────────────────────────│                          │
       │                         │                          │
```

## 数据面响应格式（data_json）

```json
{
  "version": 1717740000,
  "routes": [
    {
      "routeId": "route-user-api",
      "clusterId": "user-cluster",
      "match": {
        "path": "/api/users/{**catch-all}",
        "hosts": ["api.example.com"]
      },
      "enabled": true
    }
  ],
  "clusters": [
    {
      "clusterId": "user-cluster",
      "loadBalancingPolicy": "RoundRobin",
      "destinations": [
        {
          "id": "user-cluster-1",
          "address": "http://localhost:5001",
          "healthy": true
        }
      ]
    }
  ]
}
```

## 关键类型定义

### proto 变更
```protobuf
// 在 dataplane.proto 的 EventType 中追加
enum EventType {
    EVENT_UNSPECIFIED = 0;
    CONFIG_UPDATE = 1;
    // ... 现有类型 ...
    CONFIG_QUERY = 7;    // 新增：控制面→数据面，查询配置
    CONFIG_REPORT = 8;   // 新增：数据面→控制面，上报配置
}
```

### 控制面 InstanceConfigQueryService
```csharp
public class InstanceConfigQueryService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ConfigQueryResult>> _pendingQueries = new();
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
    
    public async Task<ConfigQueryResult> QueryConfigAsync(string instanceId) { ... }
    public void HandleConfigReport(string eventId, string dataJson) { ... }
}
```

## Risks / Trade-offs

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| CONFIG_REPORT 丢失 | 前端等待超时 | 10s 超时 + 友好错误提示 |
| 数据面 EventSubscription 未实现 CONFIG_QUERY 处理 | 所有查询超时 | 控制面返回"实例未响应"而非静默超时 |
| 大配置导致 data_json 体积过大 | gRPC 消息超限 | 保持 JSON 仅包含核心字段（Routes/Clusters），不含证书等大对象 |
| 并发查询同一实例 | 多次查询无冲突 | TCS 使用 event_id 隔离，每次查询独立 |
