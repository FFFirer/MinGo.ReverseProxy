## Why

登录页面陷入无限重定向循环：`/api/auth/me → 401 → window.location.href='/login' → 整页刷新 → 重新调用 /api/auth/me → ...`。根本原因是 API 客户端（`client.ts`）的 401 拦截器使用硬跳转，且 App 组件挂载时无条件调用 `fetchUser()`，导致即使已在登录页也会触发该循环。

## What Changes

1. **B - client.ts 401 拦截器加入路径判断**：已在 `/login` 时不执行 `window.location.href` 跳转，避免循环
2. **C - client.ts 401 拦截器移除硬跳转**：只负责清除用户状态 + throw 异常，不再做 `window.location.href` 跳转
3. **路由跳转逻辑上移**：401 的页面级跳转由调用方或路由守卫统一处理

## Capabilities

### New Capabilities

### Modified Capabilities

## Impact

- `frontend/min-go-console/src/api/client.ts` — 修改 401 拦截逻辑
