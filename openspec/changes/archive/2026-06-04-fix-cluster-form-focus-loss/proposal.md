## Why

在集群管理「添加集群」表单的目标地址输入框中，每输入一个字符输入框就会失去焦点，导致用户无法正常填写目标地址。问题的根源是使用了 SolidJS 的 `.map()` 直接渲染列表，导致每次信号更新时整个列表 DOM 被重建，input 元素被替换从而丢失焦点。

## What Changes

- 将 `Clusters.tsx` 中 `ClusterFormModal` 组件内 destinations 列表的 `.map()` 渲染替换为 SolidJS 的 `<For>` 组件
- 从 `solid-js` 导入 `For` 组件
- 保持所有现有功能和样式不变，仅修复焦点保持问题

## Capabilities

### New Capabilities

- `cluster-form-focus`: 确保集群管理表单中输入框在输入时保持焦点

### Modified Capabilities

无

## Impact

- 仅修改 `frontend/min-go-console/src/pages/Clusters.tsx` 一个文件
- 无 API/后端变更
- 无新增依赖
