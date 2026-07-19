## Context

当前前端（SolidJS + Vite + Tailwind CSS v4）所有图标使用 `<i class="fa fa-xxx">` 的 Font Awesome CSS class 语法，但 Font Awesome 字体文件/样式表从未加载——未通过 CDN、npm 包或 CSS import 引入。这导致约 41 处图标全部不可见。

现有代码中，图标全部以 `<i class="fa fa-XXX">` 形式出现，Tailwind CSS 样式类与图标 class 同层级混用（如 `<i class="fa fa-server w-6 text-center">`）。

项目仅使用了 Font Awesome 4 的免费图标（`fa-` 前缀），不涉及付费图标或 Font Awesome 5+ 的 `fas`/`far`/`fab` 前缀。

## Goals / Non-Goals

**Goals:**
- 所有图标正确显示
- 图标在不同 DPI/缩放下保持清晰（SVG 矢量渲染）
- 仅打包实际使用的图标（tree-shaking）
- 与 SolidJS + Vite + Tailwind CSS 架构原生集成
- 保持现有 UI 样式不变（尺寸、颜色、间距、悬停效果等）

**Non-Goals:**
- 不改变图标的视觉样式或设计语言（沿用 Font Awesome 外观）
- 不引入额外图标集
- 不重写页面布局或组件结构
- 不处理后端/API 层

## Decisions

| 决策 | 选择 | 备选方案 | 理由 |
|------|------|---------|------|
| 图标库 | **solid-icons** | @fortawesome/fontawesome-free（零代码改动但无 tree-shaking）、unplugin-icons（需额外 Vite 配置）、lucide-solid（所有图标名需重新映射） | 唯一同时满足：SolidJS 原生组件、Font Awesome 图标集 1:1 映射、tree-shaking、TypeScript 支持 |
| 图标集 | **solid-icons/fa** (Font Awesome) | 其他内置集（Hi/Bs/Md） | 与现有 `fa-xxx` 命名完全对应，迁移路径最短。如 `fa-server` → `<FaServer />` |
| 迁移方式 | **逐个文件替换 import + JSX** | 批量 codemod | 10 个文件 41 处替换量可控，手动替换可确保无遗漏。使用 ast-grep 辅助可提速 |
| 样式传递 | **class prop 直接传递** | styled-components / CSS Modules | `class="w-6 text-center"` 语法与现有 Tailwind 用法完全一致，保持统一风格 |

### solid-icons 组件命名映射规则

Font Awesome CSS class → solid-icons 组件名转换规则：
1. 去掉 `fa-` 前缀
2. 中划线转驼峰（PascalCase）
3. 每个首字母大写

```
fa-server        →  <FaServer />
fa-cog           →  <FaCog />
fa-bars          →  <FaBars />
fa-search        →  <FaSearch />
fa-plus          →  <FaPlus />
fa-times         →  <FaTimes />
fa-chevron-down  →  <FaChevronDown />
fa-chevron-right →  <FaChevronRight />
fa-sun-o         →  <FaSunO />
fa-moon-o        →  <FaMoonO />
fa-clock-o       →  <FaClockO />
fa-line-chart    →  <FaLineChart />
fa-list-alt      →  <FaListAlt />
fa-tachometer    →  <FaTachometer />
fa-exclamation-circle  →  <FaExclamationCircle />
fa-exclamation-triangle →  <FaExclamationTriangle />
fa-check-circle  →  <FaCheckCircle />
fa-times-circle  →  <FaTimesCircle />
fa-arrow-up      →  <FaArrowUp />
fa-edit          →  <FaEdit />
fa-trash         →  <FaTrash />
fa-key           →  <FaKey />
fa-save          →  <FaSave />
fa-upload        →  <FaUpload />
fa-undo          →  <FaUndo />
fa-refresh       →  <FaRefresh />
fa-shield        →  <FaShield />
fa-lock          →  <FaLock />
fa-random        →  <FaRandom />
fa-cubes         →  <FaCubes />
fa-bell          →  <FaBell />
fa-user          →  <FaUser />
fa-server        →  <FaServer />
```

## Risks / Trade-offs

| 风险 | 缓解措施 |
|------|---------|
| `solid-icons/fa` 中部分 `fa-xxx` 图标名可能不存在 | 预先验证全部 30 个唯一图标名。如缺失则用近似图标替换或从其他 solid-icons 内置集查找 |
| solid-icons 包的 TypeScript 定义可能与当前 tsconfig 不兼容 | 安装后立即 `pnpm build` 验证 |
| 组件替换时可能遗漏某个 icon class 属性（如 `mt-0.5` 未保留） | 逐一对比渲染结果，确保 Tailwind class 完整传递 |
| 图标尺寸（`w-*`/`h-*`/`text-*`）在 SVG 模式下与字体模式下表现可能有细微差异 | solid-icons 组件默认 1em 大小，与 Font Awesome 字体行为一致 |
| `solid-icons` 停止维护 | 当前有 800+ stars，活跃维护。如需要可降级到 `unplugin-icons` 作为后备方案 |
