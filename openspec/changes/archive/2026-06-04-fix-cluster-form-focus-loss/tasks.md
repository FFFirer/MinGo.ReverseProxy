## 1. 修复 clusters 表单焦点丢失

- [x] 1.1 在 `Clusters.tsx` 的 `solid-js` import 中添加 `For`
- [x] 1.2 将 destinations 列表的 `.map()` 渲染替换为 `<For>` 组件

## 2. 全局替换 .map() 为 <For>

- [x] 2.1 `Certificates.tsx` — `certs().map` → `<For>`
- [x] 2.2 `Dashboard.tsx` — `requestMetrics().map` → `<For>`
- [x] 2.3 `Instances.tsx` — `instances().map` → `<For>`
- [x] 2.4 `Logs.tsx` — `logs().map` → `<For>`
- [x] 2.5 `Monitoring.tsx` — `alerts().map` → `<For>`
- [x] 2.6 `Routes.tsx` — `filteredRoutes().map` + `props.clusters.map` → `<For>`
- [x] 2.7 `Toast.tsx` — `items.map` → `<For>`
- [x] 2.8 `Clusters.tsx` — `clusters().map` (外层集群卡片) → `<For>`

## 3. 验证

- [x] 3.1 `pnpm build` 通过
