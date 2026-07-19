## 1. 重构 auth store 信号导出

- [x] 1.1 在 `store/auth.ts` 中确保 `setUser` 被导出，供 `api/client.ts` 在 401 时调用
- [x] 1.2 确保 `setUser` 的类型签名正确

## 2. 统一 api client 的 401 处理

- [x] 2.1 在 `api/client.ts` 中导入 `setUser`，将 401 处理改为 `setUser(null)` + `throw`，移除 `window.location.href`
- [x] 2.2 验证修改后的 `request()` 函数 401 分支逻辑正确

## 3. 让 fetchUser 复用统一 api client

- [x] 3.1 修改 `store/auth.ts` 中的 `fetchUser()`，使用 `api.get<UserInfo>('/auth/me')` 替代独立的 `fetch()` 调用
- [x] 3.2 处理异常情况：401 由 api client 统一处理，`fetchUser` 的 catch 中仅 `setUser(null)` + `setLoading(false)`

## 4. 添加 visibilitychange 自动重验证

- [x] 4.1 在 `App.tsx` 中添加 `visibilitychange` 事件监听，页面可见时调用 `fetchUser()`
- [x] 4.2 确保 cleanup 正确移除事件监听
