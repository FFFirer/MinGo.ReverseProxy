## Context

当前 MinGo Console 前端使用 SolidJS + Tailwind CSS v4，无第三方 UI 组件库。按钮样式通过 `app.css` 中 `@layer components` 定义的自定义类实现（`.btn` / `.btn-primary` / `.btn-secondary` / `.btn-danger`）。

操作按钮存在三种不一致的模式：
1. 集群卡片底部：使用 `btn btn-secondary`（图标+文字，灰底背景）
2. 路由/证书表格操作列：仅使用 `text-primary` / `text-danger` 纯色图标（无背景，无文字）
3. 集群弹窗目标列表删除按钮：纯 `fa fa-times` 图标（无背景，无提示）

用户期望根据空间场景使用三种明确模式：紧凑表单→仅图标+tooltip、宽松大面积→图标+文字、宽松小面积→仅文字。

## Goals / Non-Goals

**Goals:**
- 为三种场景定义清晰的 CSS 组件类：`btn-icon`、`btn-text`（以及各自的 primary/danger 变体）
- 集群页弹窗：目标列表删除和添加按钮改为紧凑图标+原生 `title` tooltip
- 路由/证书管理表操作列：编辑/删除改为纯文字按钮
- 集群卡片底部操作按钮统一为 `btn-text` 风格对齐
- 所有操作按钮 hover 时有轻量背景色浮现（`hover:bg-{color}/10`）

**Non-Goals:**
- 不改变页面布局结构
- 不引入第三方 UI 库
- 不改动页面标题区的主要操作按钮（「添加集群」「添加路由」等已符合图标+文字规范）
- 不改动弹窗底部的「取消」「保存」按钮

## Decisions

### 1. CSS 组件类设计

在 `@layer components` 中新增两类按钮：

**紧凑图标按钮 `.btn-icon`**（用于表单紧凑区域）：
- `p-2 rounded-lg text-sm cursor-pointer transition-colors duration-200`
- 纯图标，无内边距文字
- 变体：`.btn-icon-primary`（`text-primary hover:bg-primary/10`）、`.btn-icon-danger`（`text-danger hover:bg-danger/10`）

**纯文字按钮 `.btn-text`**（用于表格操作列、卡片操作区）：
- `px-3 py-1.5 rounded-lg text-sm font-medium cursor-pointer transition-colors duration-200`
- 带文字标签，无默认背景色
- 变体：`.btn-text-primary`、`.btn-text-danger`

### 2. Tooltip 方案

使用原生 HTML `title` 属性实现 tooltip，零依赖。适用于紧凑场景的图标按钮。

### 3. 图标保留策略

| 区域类型 | 图标 | 文字 | tooltip |
|----------|------|------|---------|
| 紧凑表单（图标按钮） | ✅ `fa` | ❌ | ✅ `title` |
| 宽松大面积（卡片操作） | ✅ `fa` | ✅ 中文标签 | ❌ |
| 宽松小面积（表格操作） | ❌ | ✅ 中文标签 | ❌ |

## Risks / Trade-offs

- **原生 title 样式不可控**：浏览器默认 tooltip 样式无法自定义，但胜在零依赖、零实现成本。可接受。
- **纯文字按钮在表格中可能宽度不一致**：中文「编辑」和「删除」宽度差异不大，且使用了相同的 `px-3 py-1.5` 内边距，视觉上平衡。
