## Context

集群管理表单（`Clusters.tsx`）中的 `ClusterFormModal` 组件包含一个「目标地址」列表，用户可以为每个目标填写名称和地址。当前实现直接使用 `.map()` 渲染 destinations 信号数组：

```tsx
{destinations().map((dest, idx) => (
  <input value={dest.address} onInput={...} />
))}
```

在 SolidJS 中，`.map()` 直接放在 JSX 中会在其依赖的信号（`destinations()`）更新时重新执行整个表达式，导致整个列表的 DOM 节点被重建，当前聚焦的 `<input>` 元素被替换，从而丢失焦点。

## Goals / Non-Goals

**Goals：**
- 修复目标地址输入框输入时失去焦点的问题
- 使用 SolidJS 推荐的 `<For>` 组件进行列表渲染
- 保持所有现有功能、样式、交互行为不变

**Non-Goals：**
- 不改动后端代码
- 不改动其他页面
- 不升级/新增依赖
- 不重写表单逻辑

## Decisions

| 决策 | 选择 | 替代方案 | 理由 |
|------|------|----------|------|
| 列表渲染方式 | `<For>` 组件 | `.map()` | `<For>` 使用 keyed reconciliation，只更新变化的条目而非重建整个列表，保持 input 的 DOM 引用和焦点状态。 |
| idx 访问方式 | `idx()` 函数调用 | 无 | `<For>` 提供的索引是 Signal 访问器（返回 `number`），传入 `updateDestination` 需调用 `idx()`。 |
| 列表 key | 使用条目本身的引用 | 自定义 key | `<For>` 默认按数组位置匹配，对静态列表足够；如需更精细控制可用 `<For each={destinations()} by={(a, b) => a.id === b.id}>`。 |

## Risks / Trade-offs

- 低风险：改动范围极小，仅一个文件中的列表渲染方式
- 如果后续有排序/插入操作，需考虑 `<For>` 的 key 策略，但目前仅为静态表单添加/删除行，无此问题
