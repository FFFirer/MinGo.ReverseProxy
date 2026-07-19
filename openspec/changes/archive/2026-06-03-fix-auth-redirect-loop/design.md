## Context

前端登录页陷入无限重定向循环：

```
App 挂载 → createEffect → fetchUser()
  → api.get('/auth/me') → 401
  → client.ts 拦截器: setUser(null); window.location.href='/login'
  → 整页刷新 → 回到 App 挂载 → ...
```

两个共同原因：
- `client.ts` 的 401 拦截器使用 `window.location.href` 硬跳转到 `/login`，导致整页重刷
- 硬跳转后 App 重新挂载，`fetchUser()` 再次被调用，形成死循环

## Goals / Non-Goals

**Goals:**
- 消除登录页的无限重定向循环
- 401 时不再做 `window.location.href` 硬跳转
- 已经在 `/login` 路径时不触发额外跳转

**Non-Goals:**
- 不改变后端认证逻辑（AuthController / AuthService）
- 不改变路由守卫 `ProtectedLayout` 的行为
- 不修改 `fetchUser()` 或 `App.tsx` 的 createEffect 逻辑

## Decisions

1. **B + C 组合方案**
   - **B**（防御性检查）：`client.ts` 的 401 拦截器判断 `window.location.pathname !== '/login'` 时才跳转
   - **C**（架构改进）：将硬跳转从 `client.ts` 移除，只保留 `setUser(null)` + `throw`，页面级跳转由上层处理

   选择原因：B 提供最直接的防御（即使有遗漏的调用路径也不会循环），C 是更干净的架构——API 客户端不该负责路由跳转。

2. **不修改 App.tsx 的 createEffect**
   - 虽然 createEffect 无条件调用 fetchUser 也是循环成因之一，但移除 `client.ts` 的硬跳转后，循环自然打破
   - `fetchUser()` 的 try/catch 会在 401 时设置 `setUser(null)` 和 `setLoading(false)`，`ProtectedLayout` 会正确地重定向到 `/login`（通过 SPA 路由，不触发整页刷新）

## Risks / Trade-offs

- [低风险] 如果其他代码依赖 `client.ts` 的 401 自动跳转行为 → 这些路径可能需要检查。当前代码库中唯一在 401 后需要跳转的是 `ProtectedLayout`，它已经通过 `loading()` / `user()` 信号处理。
- [低风险] 移除硬跳转后，401 时用户停留在当前页，显示的是空白/错误状态 → `fetchUser()` 的 catch 已设置 `user=null, loading=false`，`ProtectedLayout` 会响应式地跳转到 `/login`。
