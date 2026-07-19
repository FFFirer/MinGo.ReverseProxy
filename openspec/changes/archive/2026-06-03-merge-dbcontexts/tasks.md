## 1. 创建 AppDbContext

- [x] 1.1 创建 `AppDbContext.cs`，继承 `IdentityDbContext<IdentityUser>`，包含 API 实体和 Identity 自定义表名
- [x] 1.2 更新 `ApiDbService.cs` → 注入 `AppDbContext`
- [x] 1.3 更新 `CertificateManager.cs` → 使用 `AppDbContext`
- [x] 1.4 更新 `Program.cs` → 只注册 `AppDbContext`
- [x] 1.5 更新 `ControlPlane.Dockerfile` → efbundle 使用 `-c AppDbContext`
- [x] 1.6 删除旧文件：`ApiDbContext.cs`、`ApplicationDbContext.cs`、`ApiDbContextDesignTimeFactory.cs`

## 2. 迁移重置

- [x] 2.1 删除旧的 `Migrations/` 和 `Migrations/ApplicationDb/` 目录
- [x] 2.2 生成新的初始迁移 `dotnet ef migrations add InitialCreate -c AppDbContext`
- [x] 2.3 验证构建：`dotnet build src/MinGo.ControlPlane.Api`
