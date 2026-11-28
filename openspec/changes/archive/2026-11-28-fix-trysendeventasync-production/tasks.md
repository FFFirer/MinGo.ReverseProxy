## 1. 修复 TrySendEventAsync 生产环境装配

- [ ] 1.1 将 `Program.cs` 中 `TrySendEventAsync` 回调的 3 行装配代码从 `if (app.Environment.IsDevelopment())` 块中移出，放在 `if` 块之后、`app.UseRouting()` 之前
- [ ] 1.2 验证改动：确认 `Development` 块只保留数据库迁移和种子数据逻辑
- [ ] 1.3 `dotnet build` 验证编译通过
