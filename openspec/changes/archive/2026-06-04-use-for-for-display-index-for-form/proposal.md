## Why

SolidJS 中 `<For>` 和 `<Index>` 有各自的适用场景：展示列表用 `<For>`，表单编辑列表用 `<Index>`。当前 `Clusters.tsx` 第 90 行在展示用途的集群卡片 destinations 列表中误用了 `<Index>`，导致语义不一致且产生了不必要的 accessor 调用 `dest()`。

## What Changes

- 将 `Clusters.tsx` 第 90 行的 `<Index each={cluster.destinations}>` 改为 `<For each={cluster.destinations}>`
- 连带将 `dest()` 改为 `dest`（<For> 传入的是值而非 accessor）

## Capabilities

### New Capabilities

- `for-index-convention`: SolidJS 组件使用规范，展示列表用 `<For>`，表单编辑用 `<Index>`

### Modified Capabilities

无

## Impact

- 仅修改 `Clusters.tsx` 一个文件
