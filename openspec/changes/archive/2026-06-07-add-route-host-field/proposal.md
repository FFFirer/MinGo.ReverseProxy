## Why

路由管理前端缺少"匹配域名（Host）"字段的展示与编辑，导致用户无法通过前端配置按域名匹配的路由规则。后端模型和数据链路已完整支持此字段，仅前端未接入。

同时发现控制面配置增量广播（`BroadcastConfigUpdateAsync`）遗漏了 `MatchHost` 字段的传播，导致增量推送时数据面丢失路由的域名匹配规则。

## What Changes

1. **Route.tsx 表格增加"匹配域名"列** — 在"路径"和"集群"之间插入列，展示 `route.match?.host`
2. **RouteFormModal 增加"匹配域名"输入框** — 编辑/新建路由时可填写 Host
3. **保存时传递 Host** — `match` 对象包含 `host` 字段
4. **搜索支持 Host** — 搜索关键词同时匹配路由名称、路径和域名
5. **修复 BroadcastConfigUpdateAsync 遗漏 MatchHost** — 增量广播时补上 `route.Match?.Host` 的传播

## Capabilities

### New Capabilities
- `route-host-field`: 路由管理中匹配域名字段的展示与编辑能力

### Modified Capabilities

无。此为前端展示层补充和后端 Bug 修复，不涉及规约级别的行为变更。

## Impact

- **前端**: `frontend/min-go-console/src/pages/Routes.tsx` — 表格列、表单字段、搜索逻辑、保存数据
- **后端**: `src/MinGo.ControlPlane.Api/GrpcServices/ConfigReplicationService.cs` — `BroadcastConfigUpdateAsync` 方法增加 `MatchHost` 赋值
