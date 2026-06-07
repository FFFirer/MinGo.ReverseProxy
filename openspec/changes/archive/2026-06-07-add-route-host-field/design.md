## Context

路由管理前端页面 `Routes.tsx` 展示了路由的表格列表和编辑/新建表单。后端 `RouteMatch.Host` 字段已完整支持，包括：

- 模型层：`RouteConfig.Match.Host` 已定义
- 持久化：通过 `MatchJson` JSON 序列化自动存储
- gRPC 同步：proto 中 `match_host = 5` 已定义，`BuildConfigSnapshotAsync` 已正确赋值
- 数据面：`DataPlaneConfigProvider` 已将 `MatchHost` 映射为 YARP 的 `RouteMatch.Hosts[]`

前端现状：类型定义 `RouteMatch.host?` 已存在，但 `Routes.tsx` 中未使用。

后端 Bug：`BroadcastConfigUpdateAsync` 增量广播时未设置 `MatchHost`，导致数据面在增量更新后丢失域名匹配规则。

## Goals / Non-Goals

**Goals:**
- 路由表格中展示"匹配域名"列
- 路由编辑/新建表单中可填写"匹配域名"
- 搜索功能支持按域名过滤
- 修复增量广播中 `MatchHost` 缺失的 Bug

**Non-Goals:**
- 不修改后端模型或数据库 schema
- 不修改 gRPC proto 定义
- 不修改数据面配置处理逻辑
- 不涉及 `RouteMatch.Headers` 等其他匹配字段的前端展示

## Decisions

| 决策 | 方案 | 理由 |
|------|------|------|
| Host 为空时展示 `-` | 沿用 Path 为空时的展示方式 | 保持 UI 一致性，避免空单元格 |
| 表单中 Host 为可选输入 | 后端 `Host` 为 `string?`，非必填 | 多数路由仅按路径匹配，不需强制填写域名 |
| 搜索同时匹配 name/path/host | 三字段任意匹配 | 用户可能通过域名搜索路由 |
| Bug 修复：BroadcastConfigUpdateAsync 中补全 MatchHost | 参照 `BuildConfigSnapshotAsync` 的实现方式 | 两个方法应保持一致的序列化逻辑 |

变更范围仅涉及两个文件：

```
frontend/min-go-console/src/pages/Routes.tsx       ← 前端（4 处改动）
src/MinGo.ControlPlane.Api/GrpcServices/ConfigReplicationService.cs  ← 后端（1 处改动）
```

## Risks / Trade-offs

- **前端改动集中在单文件**：Routes.tsx 约 226 行，改动点清晰，风险低
- **增量广播修复后仍保持向后兼容**：数据面已支持 `hosts` 为 null，新增赋值不会破坏现有行为
- **无明显性能影响**：Host 字段为简单字符串，不涉及复杂计算或额外网络请求
