## Why

`RouteTransforms` 当前是一个空壳模型 — 定义了 `PathPattern`/`PathPrefix` 字典但格式不兼容 YARP，数据面两处 `IProxyConfigProvider` 实现都直接忽略它，前端也没有任何配置 UI。用户配置的 transforms 在 DB → gRPC → DataPlane 全链路中被静默丢弃，任何路径重写或 header 操作都无法生效。

## What Changes

- **数据模型重写**：`RouteTransforms` 类替换为 `List<Dictionary<string, string>>` — 直接映射 YARP 原生 transform 列表格式，消除转换层
- **Transform Schema Registry**：新增 `GET /api/transforms/schemas` 端点，返回所有已知 transform 类型的字段 schema，供前端动态渲染配置表单
- **数据面修复**：`DataPlaneConfigProvider.ApplyConfig()` 和 `DatabaseProxyConfigProvider` 两处在构建 `YarpRouteConfig` 时正确传递 `Transforms` 属性
- **前端 Transform 配置 UI**：RouteFormModal 新增可折叠的 transforms 配置面板，采用三步流程（列表 → 选类型 → 配置 → 确认），完全由 schema 驱动渲染，支持路径重写、请求/响应头操作、X-Forwarded 头控制
- **向后兼容**：旧 `"{}"` JSON 对象格式的 DB 数据被静默降级为空 transforms，不影响已有路由

## Capabilities

### New Capabilities
- `route-transform-schema`: Transform schema registry API and schema-driven frontend configuration UI for route request/response transforms, including path rewriting, header manipulation, and X-Forwarded header control

### Modified Capabilities
- `grpc-config-replication`: `ConfigSnapshot.RouteConfig.transforms_json` 字段现在被数据面正确读取并应用到 YARP 路由配置中；新增 transforms 变更不会影响现有 gRPC 协议

## Impact

- **Core 模型** (`src/MinGo.Core/Models/GatewayConfig.cs`)：`RouteTransforms` 类移除，`RouteConfig.Transforms` 改为 `List<Dictionary<string, string>>?`
- **DB 实体** (`src/MinGo.Core/Entities/ProxyConfigEntities.cs`)：`TransformsJson` 默认值改为 `"[]"`，`GetTransforms()` 返回新类型
- **新增 Core 组件** (`src/MinGo.Core/Transforms/TransformSchemaRegistry.cs`)：Transform schema 定义和注册
- **ControlPlane API**：新增 `GET /api/transforms/schemas` 端点
- **ConfigReplication gRPC**：无需改 proto，`transforms_json` 字段序列化自动适配新类型
- **DataPlane** (`DataPlaneConfigProvider.cs`)：应用 transforms 到 YARP 路由配置
- **Infrastructure** (`DatabaseProxyConfigProvider.cs`)：同上修复
- **前端** (`types/index.ts`, `pages/Routes.tsx`)：类型更新 + schema-driven transforms 配置 UI（三步流程：卡片列表 → 类型选取 → 字段配置）
- **无新依赖**：YARP 2.3.0 已经原生支持所有涉及 transform 类型
