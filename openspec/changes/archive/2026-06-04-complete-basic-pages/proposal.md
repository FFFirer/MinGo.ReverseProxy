## Why

当前前端页面（Routes、Clusters、Certificates）功能不完整：Routes 缺少集群下拉选择和操作反馈，Clusters 的添加按钮无功能、缺少编辑和健康检查配置界面，Certificates 仅有只读列表无上传/编辑/删除操作。此外所有页面缺少统一的错误处理机制和端到端测试覆盖。

## What Changes

- **Toast 错误处理组件**：创建全局通知组件，所有页面统一显示操作成功/失败消息
- **Clusters 页面补全**：添加/编辑 Modal（含目标管理、健康检查配置）、错误反馈
- **Certificates 页面补全**：上传（文件）、手动添加、编辑、删除、错误反馈
- **Routes 页面增强**：clusterId 文本输入改为集群下拉选择、操作后 Toast 反馈
- **Playwright E2E 测试**：安装 Playwright，为 Routes/Clusters/Certificates 编写端到端测试

## Capabilities

### New Capabilities
- `toast-notification`: 全局 Toast 消息通知组件，支持成功/错误/警告类型
- `e2e-testing`: Playwright 端到端测试基础设施和测试用例

### Modified Capabilities
- （无，现有 specs 不涉及需求变更，仅补全前端实现）

## Impact

- 前端文件：修改 `src/pages/Routes.tsx`、`src/pages/Clusters.tsx`、`src/pages/Certificates.tsx`，新增 `src/components/shared/Toast.tsx`
- 依赖：新增 `@playwright/test` 开发依赖
- 后端 API：无需修改，复用现有 `ApiManagementController`（routes/clusters）和 `CertificatesController`
