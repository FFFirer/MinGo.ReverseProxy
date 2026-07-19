## 1. Dependencies & Setup

- [x] 1.1 Install `solid-icons` npm package via `pnpm add solid-icons`
- [x] 1.2 Run `pnpm build` 验证安装后构建无报错

## 2. Header 图标替换

- [x] 2.1 在 `src/components/layout/Header.tsx` 中添加所需的 solid-icons import 语句
- [x] 2.2 替换 Header 中所有 `<i class="fa fa-xxx">` 为对应 `<FaXxx />` 组件（7 处：fa-bars, fa-search, fa-sun-o, fa-moon-o, fa-bell, fa-user, fa-times）

## 3. Sidebar 图标替换

- [x] 3.1 在 `src/components/layout/Sidebar.tsx` 中添加所需的 solid-icons import 语句
- [x] 3.2 替换 Sidebar navItems 中 9 个图标（fa-tachometer, fa-random, fa-server, fa-shield, fa-lock, fa-line-chart, fa-list-alt, fa-cubes, fa-cog）
- [x] 3.3 更新图标渲染处 `<i class="fa ${item.icon}">` 为动态组件渲染

## 4. Toast 图标替换

- [x] 4.1 在 `src/components/shared/Toast.tsx` 中添加所需的 solid-icons import 语句
- [x] 4.2 替换 typeIcons 映射中的图标（fa-check-circle, fa-times-circle, fa-exclamation-triangle）
- [x] 4.3 替换关闭按钮图标（fa-times）

## 5. Dashboard 页面图标替换

- [x] 5.1 在 `src/pages/Dashboard.tsx` 中添加所需的 solid-icons import 语句
- [x] 5.2 替换 5 处图标（fa-arrow-up, fa-refresh, fa-exclamation-circle, fa-clock-o, fa-server）

## 6. Clusters 页面图标替换

- [x] 6.1 在 `src/pages/Clusters.tsx` 中添加所需的 solid-icons import 语句
- [x] 6.2 替换 6 处图标（fa-plus×2, fa-edit, fa-trash, fa-times, fa-chevron-right/fa-chevron-down）

## 7. 其余页面图标替换

- [x] 7.1 替换 `src/pages/Routes.tsx` 中图标（fa-plus, fa-search）
- [x] 7.2 替换 `src/pages/Certificates.tsx` 中图标（fa-plus, fa-upload）
- [x] 7.3 替换 `src/pages/Security.tsx` 中图标（fa-key, fa-plus, fa-save）
- [x] 7.4 替换 `src/pages/Settings.tsx` 中图标（fa-undo, fa-save）
- [x] 7.5 替换 `src/pages/Logs.tsx` 中图标（fa-search, fa-refresh）

## 8. 构建验证

- [x] 8.1 运行 `pnpm build` 确认无 TypeScript 或构建错误
- [x] 8.2 `pnpm dev` 在浏览器中逐页验证所有图标正确显示
