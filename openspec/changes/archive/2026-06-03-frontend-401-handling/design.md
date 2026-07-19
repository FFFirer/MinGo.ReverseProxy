## Context

当前前端认证架构使用 ASP.NET Core Identity Cookie 认证。前端有两个独立的 HTTP 请求路径：

1. **通用 API 客户端** (`api/client.ts`): 所有业务请求通过此模块，使用 `fetch()` + `credentials: 'include'`。401 时执行 `window.location.href = '/login'`（全页硬跳转）。
2. **认证状态管理** (`store/auth.ts`): `fetchUser()` 使用独立的 `fetch()` 调用 `/api/auth/me`，401 时仅 `setUser(null)`，不触发导航。

两者不一致导致行为分裂：业务 API 调用 401 会硬刷新页面，而 `fetchUser()` 返回 401 则静默处理。目标是将两者统一为 SPA 内导航，避免状态丢失。

## Goals / Non-Goals

**Goals:**
- 所有 API 调用的 401 响应统一触发认证状态重置和 SPA 导航
- `fetchUser()` 复用通用 API 客户端，不再有独立的 fetch 逻辑
- 添加 `visibilitychange` 监听，页面从后台切回时自动重验证
- 移除 `window.location.href` 硬跳转，所有导航通过 SolidJS Router 的 `navigate()` 完成

**Non-Goals:**
- 不改变后端认证机制（保持 Cookie 认证）
- 不添加 token refresh 逻辑
- 不修改后端 API 响应格式
- 不涉及权限/角色检查（仅处理登录/未登录状态）

## Decisions

### Decision 1: 事件总线方式触发 SPA 导航

**选项对比：**

| 方案 | 描述 | 优点 | 缺点 |
|------|------|------|------|
| **A: 响应式信号 (选定)** | 401 时调用 `setUser(null)`，`ProtectedLayout` 响应式导航 | 无新依赖；和现有模式一致；最简单 | 需要在模块级引用 auth store |
| **B: 自定义事件** | 401 时 dispatch 自定义事件，App 组件监听后导航 | 解耦彻底 | 额外事件机制；模块间隐式耦合 |
| **C: navigate 回调注入** | api client 接受 navigate 函数作为依赖 | 显式依赖 | 配置复杂；模块级不方便 |

**选择 A**，因为：
- `setUser(null)` 和 `ProtectedLayout` 的 `!user()` 检查已经形成了响应式链路
- 改动最小，不需要新的事件体系
- `api/client.ts` 已经是一个模块了，引入 `setUser` 是单向数据流

### Decision 2: `fetchUser()` 复用 api client

直接用 `api.get<UserInfo>('/auth/me')` 替换 `auth.ts` 中的独立 `fetch()` 调用。

这样 `/api/auth/me` 的 401 会自然进入统一拦截器 -> `setUser(null)` -> 响应式导航。

### Decision 3: `visibilitychange` 定期验证

在 `App.tsx` 中添加 `visibilitychange` 事件监听，当页面从后台切回前台时调用 `fetchUser()` 重新验证 cookie 有效性。无需轮询，节省资源。

## Risks / Trade-offs

- **[风险] `api/client.ts` 中 `throw` 后的执行流**：`setUser(null)` 后依然 `throw Error`，调用者需 catch。某些场景可能 catch 后做了错误恢复，导致状态冲突。→ 约定：401 由拦截器全权处理，调用者 catch 后不应再处理认证相关逻辑。
- **[风险] 模块循环引用**：`api/client.ts` 导入 `store/auth.ts` 的 `setUser`，而 `store/auth.ts` 导入 `api/client.ts`。→ 将 `setUser` 提取到独立的信号定义文件，或确保导入链是单向的。
- **[权衡] 仅前端改动**：后端无需任何修改，认证机制保持不变。
