## Why

当前前端所有图标使用 `<i class="fa fa-xxx">`（Font Awesome CSS class 语法），但 Font Awesome 从未被安装或加载——未通过 CDN、npm 包或 CSS import 引入。这导致 10 个文件约 41 处图标全部无法显示。另外，CSS 字体图标方式在不同缩放级别下可能出现锯齿/模糊，不是一个可靠的前端图标方案。

## What Changes

1. 安装 `solid-icons` npm 包（SolidJS 原生 SVG 图标库）
2. 使用其内置的 Font Awesome 系列（`solid-icons/fa`），与现有 `fa-xxx` 命名保持 1:1 映射关系
3. 替换 10 个文件中所有 `<i class="fa fa-xxx">` 为 `<FaXxx />` 组件语法
4. Tailwind CSS class 通过组件 `class` prop 直接传递，保持现有样式不变
5. 移除 Font Awesome CSS 相关的隐式依赖

## Capabilities

### New Capabilities
- `icon-system`: 统一的、SolidJS 原生的 SVG 图标系统。所有图标以 SolidJS 组件形式存在，接收 `class` prop 以支持 Tailwind CSS 样式，通过 Vite tree-shaking 仅打包使用到的图标。

### Modified Capabilities
> 无现有 spec 需要修改。

## Impact

- **新增依赖**: `solid-icons`（开发/生产依赖）
- **修改文件（10 个）**:
  - `src/components/layout/Header.tsx`（7 处）
  - `src/components/layout/Sidebar.tsx`（9 处）
  - `src/components/shared/Toast.tsx`（4 处）
  - `src/pages/Dashboard.tsx`（5 处）
  - `src/pages/Routes.tsx`（1 处）
  - `src/pages/Clusters.tsx`（6 处）
  - `src/pages/Certificates.tsx`（2 处）
  - `src/pages/Security.tsx`（3 处）
  - `src/pages/Settings.tsx`（2 处）
  - `src/pages/Logs.tsx`（2 处）
- **无 API/后端变更**
- **无样式表变更**（Tailwind class 保持不动）
