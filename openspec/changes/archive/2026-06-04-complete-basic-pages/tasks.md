## 1. Toast 通知组件

- [x] 1.1 创建 `src/store/toast.ts` - 全局 Toast 信号（类型、消息、自动消失）
- [x] 1.2 创建 `src/components/shared/Toast.tsx` - Toast 渲染组件（多类型、动画）
- [x] 1.3 在 `src/App.tsx` 中挂载 Toast 组件

## 2. Clusters 页面补全

- [x] 2.1 实现"添加集群" Modal（名称、负载均衡策略、目标地址初始配置）
- [x] 2.2 实现"编辑集群" Modal（编辑名称、策略、目标列表增删、健康检查配置）
- [x] 2.3 替换空 catch 为 Toast 错误提示

## 3. Certificates 页面补全

- [x] 3.1 实现"上传证书"功能（文件选择、域名输入、FormData 提交到 POST /api/certificates/upload）
- [x] 3.2 实现"手动添加"证书 Modal
- [x] 3.3 实现"编辑"证书 Modal
- [x] 3.4 实现"删除"证书功能
- [x] 3.5 替换空 catch 为 Toast 错误提示

## 4. Routes 页面增强

- [x] 4.1 将 clusterId 文本输入改为从 `/api/apimanagement/clusters` 加载的下拉选择
- [x] 4.2 添加操作成功/失败 Toast 反馈（保存、删除）
- [x] 4.3 添加表单验证（必填字段、路径格式）

## 5. Playwright E2E 测试

- [x] 5.1 安装 Playwright 依赖并初始化配置
- [x] 5.2 编写登录流程测试
- [x] 5.3 编写 Routes 页面 CRUD 操作测试
- [x] 5.4 编写 Clusters 页面 CRUD 操作测试
- [x] 5.5 编写 Certificates 页面 CRUD 操作测试
