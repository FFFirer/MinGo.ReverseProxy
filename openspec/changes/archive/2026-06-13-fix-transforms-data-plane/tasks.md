## 1. Backend — 数据模型重构

- [ ] 1.1 `src/MinGo.Core/Models/GatewayConfig.cs`：将 `RouteConfig.Transforms` 类型从 `RouteTransforms` 改为 `List<Dictionary<string, string>>?`，删除 `RouteTransforms` 类
- [ ] 1.2 `src/MinGo.Core/Entities/ProxyConfigEntities.cs`：`TransformsJson` 默认值改为 `"[]"`；`GetTransforms()` 返回 `List<Dictionary<string, string>>?`，添加旧 `"{}"` 格式降级
- [ ] 1.3 `src/MinGo.Infrastructure/Data/ApiDbService.cs`：更新 `MapToRouteModel()`、`MapToRouteEntity()`、`UpdateRouteAsync()` 中的 transforms 序列化/反序列化逻辑

## 2. Backend — Transform Schema Registry

- [ ] 2.1 `src/MinGo.Core/Transforms/TransformSchemaRegistry.cs`（新增）：定义 `TransformSchema`、`TransformFieldSchema` 模型类和静态 `TransformSchemaRegistry.Schemas` 列表，注册 PathPrefix / PathPattern / RequestHeader / ResponseHeader / XForwarded 五个 schema
- [ ] 2.2 `src/MinGo.ControlPlane.Api/Controllers/TransformsController.cs`（新增）：实现 `GET /api/transforms/schemas` 端点，返回 registry 中的 schemas

## 3. Backend — 数据面修复

- [ ] 3.1 `src/MinGo.DataPlane/ConfigSync/DataPlaneConfigProvider.cs`：`ApplyConfig()` 中构建 `YarpRouteConfig` 时添加 `Transforms = route.GetTransforms()`
- [ ] 3.2 `src/MinGo.Infrastructure/ExternalServices/DatabaseProxyConfigProvider.cs`：`LoadConfigFromDatabaseAsync()` 中构建 `YarpRouteConfig` 时添加 `Transforms = route.Transforms`

## 4. Backend — gRPC ConfigReplication 适配

- [ ] 4.1 `src/MinGo.ControlPlane.Api/GrpcServices/ConfigReplicationService.cs`：确认 `JsonSerializer.Serialize(route.Transforms)` 对新格式的输出正确（新模型序列化后直接为 JSON 数组，继续写入 `transforms_json` proto 字段）
- [ ] 4.2 验证 gRPC 推送链路：routes 中的 transforms 正确序列化 → 网络传输 → DataPlane 端正确反序列化 → 赋值到 YARP RouteConfig

## 5. Frontend — 类型和 API

- [ ] 5.1 `frontend/min-go-console/src/types/index.ts`：更新 `RouteTransforms` 类型为 `Record<string, string>[]`；添加 `TransformSchema`、`TransformFieldSchema` 接口定义
- [ ] 5.2 `frontend/min-go-console/src/api/client.ts`：添加 `getTransformSchemas()` API 调用方法

## 6. Frontend — Schema-Driven Transforms UI

- [ ] 6.1 `frontend/min-go-console/src/pages/Routes.tsx`：RouteFormModal 内新增 transforms 折叠面板，实现三步状态机：
  - LIST 状态（in-flow）：transform 卡片摘要列表 + "+ 添加变换"按钮，每张卡片显示类型名和字段摘要，点击可编辑（打开 CONFIGURE 内层弹窗），末尾有删除按钮
  - PICK 状态（内层弹窗）：`absolute inset-0` 覆盖整张 Modal Card，按 category 分组展示所有可用类型，网格按钮布局，带描述，自身可滚动
  - CONFIGURE 状态（内层弹窗）：与 PICK 相同的 absolute 弹窗模式，根据所选 schema 的 `Fields` 动态渲染表单（input/select），含确认/取消按钮
- [ ] 6.2 实现 transforms 数据与路由表单的联动：编辑时从 route.transforms 加载，保存时序列化到 route.transforms
- [ ] 6.3 实现 schema-driven 渲染：`ListView`/`PickView`/`ConfigureView` 三个视图组件，字段渲染完全由 `TransformFieldSchema` 驱动

## 7. 验证

- [ ] 7.1 编译验证：所有项目（Core / ControlPlane / DataPlane / Infrastructure / ReverseProxy）通过 `dotnet build`
- [ ] 7.2 前端编译验证：`pnpm build` 通过
- [ ] 7.3 端到端功能验证：创建带 transforms 的路由 → 确认 DB 存储格式 → 推送至 DataPlane → 发送请求确认 header 正确传递
