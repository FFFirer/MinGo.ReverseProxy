## Context

当前前端（SolidJS + TailwindCSS v4 SPA）的 Routes、Clusters、Certificates 三个核心页面功能不完整。后端对应 API 已完整实现（ApiManagementController 提供 routes/clusters 完整 CRUD，CertificatesController 提供完整 CRUD + 文件上传），前端需补全缺失的交互功能。

现有模式参考：Routes.tsx 是最完整的实现，使用 `createSignal` + `onMount` + `api.get/post/put/delete` 模式，Modal 表单处理创建/编辑。

## Goals / Non-Goals

**Goals:**
- 创建全局 Toast 通知组件，替代所有页面的空 `catch` 块
- Clusters 页面：添加/编辑 Modal（含目标地址管理、健康检查配置）
- Certificates 页面：上传（文件）、手动添加、编辑、删除
- Routes 页面：集群下拉选择、操作反馈 Toast
- Playwright E2E 测试覆盖三个页面的核心操作

**Non-Goals:**
- 不修改后端 API（现有 API 已满足需求）
- 不涉及 Chart.js 可视化（后续优化阶段）
- 不修改 Login/Dashboard/Monitoring/Logs/Instances/Settings/Security 页面
- 不修改数据模型或数据库

## Decisions

### D1: 全局 Toast 组件模式
**选择**: 使用 SolidJS `createSignal` + 全局状态管理，不引入第三方 toast 库
**理由**: 项目仅有 `solid-js`、`@solidjs/router`、`chart.js` 三个依赖，为 toast 引入额外包不值当。全局信号模式与现有 `user-signal.ts` 模式一致。
**实现**: 在 `src/store/toast.ts` 创建全局信号，`src/components/shared/Toast.tsx` 渲染，`App.tsx` 中挂载

### D2: Cluster 编辑 Modal 复用 RouteFormModal 模式
**选择**: 与 Routes 页面相同的 Modal 弹窗模式
**理由**: Routes.tsx 的 Modal 模式已在项目中验证，Cluster 的目标地址列表和健康检查配置适合在 Modal 中通过子表单管理

### D3: 证书上传使用标准表单
**选择**: 在 Modal 中使用 `<input type="file">` + 文本字段，通过 `FormData` 提交
**理由**: 后端 `POST /api/certificates/upload` 使用 `IFormFile`，前端需构造 `FormData` 发送

### D4: Playwright 作为 E2E 框架
**选择**: Playwright 安装为开发依赖，测试文件放在 `frontend/min-go-console/e2e/` 目录
**理由**: Playwright 跨浏览器支持好、API 简洁、与 Vite 开发服务器配合良好

## Risks / Trade-offs

| 风险 | 缓解措施 |
|------|---------|
| Toast 全局信号可能与其他状态冲突 | 隔离到独立 store 文件，仅暴露 `addToast`/`removeToast` 方法 |
| 文件上传的 FormData 与现有 JSON API 客户端不兼容 | Certificates 页面直接使用 `fetch` 上传，不走 `api.put/post` JSON 封装 |
| E2E 测试依赖后端服务运行 | 测试配置中先启动控制面 API 或 mock 数据 |
