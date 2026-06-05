## Why

前端操作按钮在当前实现中存在可视化性不足的问题：表格操作列（路由管理、证书管理）仅使用纯色图标按钮，缺乏背景和文字标签，用户难以快速识别可操作区域；表单紧凑区域（集群编辑弹窗目标列表）的删除按钮同样缺少明确视觉反馈。这影响了管理控制台的可用性和操作效率。

## What Changes

- 新增 CSS 组件类：`.btn-icon` / `.btn-icon-primary` / `.btn-icon-danger`（紧凑图标按钮）、`.btn-text` / `.btn-text-primary` / `.btn-text-danger`（纯文字按钮）
- 集群编辑/新增页弹窗：目标地址列表删除按钮改为 `btn-icon-danger` + `title` tooltip；添加目标按钮改为 `btn-icon-primary` + `title` tooltip
- 路由管理表操作列：编辑/删除改为纯文字按钮（无图标）
- 证书管理表操作列：编辑/删除改为纯文字按钮（无图标）
- 集群卡片底部操作按钮：统一为 `btn-text` 风格对齐

## Capabilities

### New Capabilities

- `button-visibility`: 前端操作按钮的视觉层次和交互反馈规范，覆盖紧凑图标按钮、纯文字按钮、图标+文字按钮三种模式

### Modified Capabilities

- 无（本次变更不涉及 spec 级别的需求变更，仅 UI 表现层调整）

## Impact

- `frontend/min-go-console/src/styles/app.css`：新增 6 个 CSS 组件类
- `frontend/min-go-console/src/pages/Clusters.tsx`：弹窗内目标列表操作按钮改为紧凑图标+tooltip
- `frontend/min-go-console/src/pages/Routes.tsx`：表格操作列改为纯文字按钮
- `frontend/min-go-console/src/pages/Certificates.tsx`：表格操作列改为纯文字按钮
