## Context

当前反向代理使用 YARP 2.3.0 作为运行时内核，但 transforms 配置通道完全断裂。`RouteTransforms` 模型定义了两个 Dictionary 属性（`PathPattern`、`PathPrefix`），格式与 YARP 要求的 `List<Dictionary<string, string>>` 不兼容，导致两个 `IProxyConfigProvider` 实现都跳过 transforms 设置。

前端 RouteFormModal 只有路径/域名/集群三个匹配字段，无 transforms 配置入口。transforms 的 Schema 信息（每个 transform 有哪些字段、字段类型、可选值等）目前完全不存在于系统中。

存量 DB 数据中 transforms 均为空（`"{}"`），无迁移压力。

## Goals / Non-Goals

**Goals:**
- 重建 transform 数据模型为 YARP 原生 `List<Dictionary<string, string>>` 格式
- 后端暴露 Transform Schema Registry API，动态描述所有已知 transform 的字段结构
- 数据面两处 `IProxyConfigProvider` 正确传递 transforms 到 YARP 路由配置
- 前端 RouteFormModal 内以折叠面板形式提供 schema-driven 的 transforms 配置 UI

**Non-Goals:**
- 不引入新依赖，YARP 2.3.0 已支持所有目标 transform 类型
- 不改动 gRPC proto 定义，`transforms_json` 字段已存在且序列化自动适配新类型
- 不涉及 transforms 的运行时验证（如 header 名格式校验），留给 YARP 原生处理
- 不覆盖所有 YARP 支持的 transform 类型，仅覆盖常见类型（路径/header/X-Forwarded），后续按需添加

## Decisions

### Decision 1: 数据模型直接使用 YARP 原生格式

**选项**:
- **A**: `List<Dictionary<string, string>>` — 即 YARP `IReadOnlyList<Dictionary<string, string>>?` 的直接映射
- **B**: 保持 `RouteTransforms` 类但改为 `List<RouteTransformItem>` typed 模型
- **C**: 保持现有 named property 方案

**选择 A**。理由：
- 数据面零转换开销 — 反序列化结果可直接赋值给 `YarpRouteConfig.Transforms`
- 序列化/反序列化是标准 JSON 操作，无自定义 mapper
- 增减 transform 类型不需要改 C# 模型定义
- 序列化格式与 `ConfigReplicationService` 中已有的 `JsonSerializer.Serialize(route.Transforms)` 完全兼容

选择 B 会增加一个中间类型层但无实质收益。选择 C 与 YARP 格式不兼容。

### Decision 2: 使用静态 Schema Registry + API，而非反射

**选项**:
- **A**: 静态 `TransformSchemaRegistry` 类硬编码所有 schema
- **B**: Attribute + 反射自动收集

**选择 A**。理由：
- schema 数量极少（初始 5 个类型），硬编码可读性更好
- 无运行时反射开销
- 添加新 transform 只需在列表中加一个 `TransformSchema` 条目，足够轻量
- IDEs 提供代码补全和类型检查

### Decision 3: Schema Registry 作为 ControlPlane API 暴露

Schema 数据放在 ControlPlane 项目中（`MinGo.ControlPlane.Api`），因为：
- UI 通过 ControlPlane 访问，与 routes/clusters 等配置在同一域
- 不需要下发到 DataPlane — schema 是前端渲染知识，不是运行时配置
- 无状态，可直接从静态 registry 返回，不需要缓存或 DB

### Decision 4: 前端 transforms 状态存储为扁平 YARP 列表

前端内部只维护一个 `Record<string, string>[]` 状态，与 API 格式一致。渲染时利用 schema 按 category 分组解释和渲染条目。保存时直接序列化。

不引入中间表示层，避免双向转换的复杂度和 bug 来源。

### Decision 5: 后端数据面修改仅设 Transforms 属性

`DataPlaneConfigProvider.ApplyConfig()` 和 `DatabaseProxyConfigProvider.LoadConfigFromDatabaseAsync()` 只需要插一条 `Transforms = route.GetTransforms()` 赋值，不需要额外的 transform 处理逻辑。

所有 transform 处理逻辑在 YARP 内部，数据面只是透传数据。

### Decision 6: PICK + CONFIGURE 均以内层弹窗呈现

添加和编辑 transform 都使用 **Modal-in-Modal** 模式：

```
Modal (外层)
│
├── LIST (in-flow)
│   ├── 卡片列表展示已配置的 transforms
│   ├── 点击卡片 → 打开 CONFIGURE 内层弹窗
│   └── 点击 "+ 添加变换" → 打开 PICK 内层弹窗
│
└── Inner Dialog (absolute inset-0, z-index 高于表单)
    │   覆盖整个 Modal Card，背景半透明遮罩
    │   自身可滚动 (overflow-y-auto)
    │
    ├── step === 'pick'
    │   └── PickView: 类型选择网格 (按 category 分组)
    │       选择类型 → 关闭 PICK → 打开 CONFIGURE
    │
    └── step === 'configure'
        └── ConfigureView: schema-driven 字段表单
            确认 → 写入 transforms → 关闭 → 回到 LIST
            取消 → 关闭 → 回到 LIST
```

**视觉效果**：PICK 和 CONFIGURE 都是浮在 Modal 之上的第二层弹窗，背景半透明，内容区域可滚动。LIST 始终作为背景可见，保持用户的位置感知。

**状态定义**：
- `list`：in-flow 展示 transforms 卡片列表
- `pick`：内层弹窗，展示所有可用 transform 类型
- `configure`：内层弹窗，展示选中类型的配置字段表单

**交互**：
- 内层弹窗点击背景关闭（discard），点击内容不冒泡
- PICK 选择类型后自动切换到 CONFIGURE 弹窗
- CONFIGURE 确认后写入 transforms 列表并回到 LIST

**选择原因**：
- 内层弹窗保留 list 上下文视觉锚定，用户知道自己在哪
- 不增加模态层级（始终在 RouteFormModal 内部）
- 每次只做一件事，避免 inline 编辑的信息过载
- schema-driven 渲染确保新增 transform 类型时 UI 零改动
- 卡片式摘要比展开全部字段更清晰，适合路由列表页的密度要求

## Risks / Trade-offs

- [**Schema 与 YARP 版本耦合**] → YARP 升级可能增减 transform 类型或参数。解法：Schema Registry 独立于 YARP 版本维护，升级时同步更新 registry。
- [**DB 中 transforms 序列化格式不可逆变更**] → 旧 `"{}"` 格式被降级处理后不再写回，数据会自动迁移到新格式。存量 `"{}"` 数据极少（实际为空），风险可控。
- [**前端通用渲染的局限性**] → 极端复杂的 transform（如条件式 transforms）可能无法用通用 schema 表达。解法：预留 `TransformFieldSchema.Type = "custom"` 扩展点，允许接入自定义组件。
- [**X-Forwarded presets 变更**] → 未来 YARP 新增 X-Forwarded 子类型，需同步更新 registry 中的 `defaultEntries`，同时 `defaultEntries` 变更可能影响现有用户配置的预期。解法：presets 仅作为新路由时的默认值，已有路由的 entries 不受 registry 更新影响。

## Migration Plan

1. 修改 `RouteTransforms` 模型 → `List<Dictionary<string, string>>?`
2. 更新 `ApiRouteEntity.GetTransforms()` 添加旧格式降级
3. 创建 `TransformSchemaRegistry` + `TransformsController`
4. 修复两处 `IProxyConfigProvider` 的 Transforms 设置
5. 更新前端类型
6. 前端 RouteFormModal 添加 schema-driven transforms 面板
7. 验证：创建带 transforms 的路由 → 确认 DB 存储格式 → 确认 gRPC 推送 → 确认 DataPlane 应用 → 确认请求 header 正确传递

回滚策略：回退代码即可，DB 数据不依赖新代码格式（旧 `"{}"` 和空 `"[]"` 都被安全降级）。

## Open Questions

- X-Forwarded 的默认行为：YARP 默认开启 For/Proto/Host，前端 X-Forwarded 面板的 presets 从 schema 中读取。如果用户未配置过 transforms（transforms 为空列表），YARP 保持默认行为；如果用户添加了 X-Forwarded 条目，显式条目覆盖默认。这个语义需要前端/后端共识。
