## Why

当前前端对 `/api/auth/me` 返回 401 的处理有两条不一致的路径：`api/client.ts` 使用 `window.location.href` 硬跳转（丢失 SPA 状态），而 `store/auth.ts` 的 `fetchUser()` 静默设置 user=null 且不触发导航。此外，通用 API 调用和用户认证请求使用两套独立的 fetch 逻辑，导致 401 行为不统一。需要将 401 处理统一为 SPA 内导航，避免全页刷新，并让 `fetchUser()` 复用同一个拦截器。

## What Changes

- 统一 `api/client.ts` 的 401 处理：从 `window.location.href` 硬跳转改为通过响应式信号 `setUser(null)` 触发 SPA 内导航
- 让 `fetchUser()` 复用 `api/client.ts` 的统一 HTTP 请求路径，而不是独立的 `fetch()` 调用
- 给 `ProtectedLayout` / `App.tsx` 添加 `visibilitychange` 监听，在页面从后台切回时自动重新验证登录状态
- 确保所有 API 端点的 401 都能触发统一的认证状态重置和导航

## Capabilities

### New Capabilities
- `unified-auth-error`: 统一的认证错误处理机制，覆盖所有前端 API 调用和用户认证状态管理

### Modified Capabilities
- （无现有 spec 变更）

## Impact

- **frontend/min-go-console/src/api/client.ts**: 修改 `request()` 函数中的 401 处理逻辑
- **frontend/min-go-console/src/store/auth.ts**: 修改 `fetchUser()` 使用统一的 api client；新增 `setUser` 导出供外部使用
- **frontend/min-go-console/src/App.tsx**: 添加 `visibilitychange` 监听
