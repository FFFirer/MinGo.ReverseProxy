## 1. 修复 client.ts 401 拦截器（B + C 组合方案）

- [x] 1.1 B - 在 401 拦截器中加入路径判断，已在 `/login` 时不跳转（被 C 的完整方案覆盖）
- [x] 1.2 C - 移除 `window.location.href` 硬跳转，改为只 `setUser(null)` + `throw`

## 2. 验证

- [x] 2.1 检查 LSP 诊断无报错
- [x] 2.2 验证类型正确
