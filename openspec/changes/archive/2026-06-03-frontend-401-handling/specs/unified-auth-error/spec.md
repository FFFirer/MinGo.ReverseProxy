## ADDED Requirements

### Requirement: 统一认证错误拦截
前端 SHALL 对所有 API 请求（包括 `/api/auth/me`）使用同一个 HTTP 请求入口。任何 API 请求返回 401 状态码时，SHALL 触发统一的认证状态重置。

#### Scenario: 业务 API 返回 401
- **WHEN** 任意业务 API 调用（如 `/api/routes`）返回 401
- **THEN** 系统 SHALL 清除当前用户状态 `user = null`
- **THEN** 系统 SHALL 通过 SPA 路由导航到 `/login`，不使用 `window.location.href` 硬跳转

#### Scenario: `/api/auth/me` 返回 401
- **WHEN** `fetchUser()` 调用 `/api/auth/me` 返回 401
- **THEN** 系统 SHALL 调用 `setUser(null)` 清除用户状态
- **THEN** 受保护路由 SHALL 自动导航到 `/login`

### Requirement: SPA 内导航，无硬跳转
前端 SHALL 在 401 时使用 SPA 路由导航（SolidJS Router 的 `navigate()`）代替 `window.location.href` 硬跳转，以保持 SPA 应用状态。

#### Scenario: 认证失效后导航
- **WHEN** 用户认证状态失效且收到 401 响应
- **THEN** URL SHALL 更新为 `/login`
- **THEN** 页面 SHALL NOT 进行全页刷新

### Requirement: 页面可见性变化时重新验证
前端 SHALL 在页面从后台切回前台时自动重新验证用户登录状态。

#### Scenario: 切换标签页后重验证
- **WHEN** 用户切换到其他标签页再切回
- **THEN** 系统 SHALL 自动调用 `fetchUser()` 检查 cookie 是否仍然有效
- **THEN** 如果 cookie 已过期，SHALL 自动导航到 `/login`
