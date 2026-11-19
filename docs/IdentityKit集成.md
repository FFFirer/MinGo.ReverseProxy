# IdentityKit 按需接入

控制面继续通过原生 AddIdentity、AppDbContext、IdentityRole 和 token provider 注册认证。
IdentityKit 的 AddPasswordResetAdministration 只增加密码重置管理服务和必填授权配置。
现有邮箱登录、Cookie、数据库迁移和初始化流程继续由控制面提供。

## 包还原与验证

当前使用 NuGet.org 已发布的 0.1.0-preview.4。
在仓库根目录运行下面的 PowerShell。

```powershell
dotnet restore MinGo.ReverseProxy.slnx
dotnet test MinGo.ReverseProxy.slnx --no-restore
```

## 管理操作授权

部署配置 AccountAdministration:UserIds 为获授权用户的稳定 ID 数组。
环境变量可以使用 AccountAdministration__UserIds__0、AccountAdministration__UserIds__1。
缺少配置时所有账号都无法调用新增管理操作。
部署方可在 Program.cs 中将 ManageAccountPasswords 策略替换为已有角色或资源权限策略。

使用管理员 Cookie 请求 GET /api/account-administration/csrf，保存防伪 Cookie 和 requestToken。
向 POST /api/account-administration/users/{userId}/password-reset-token 发送相同 Cookie，
并在 X-CSRF-TOKEN 请求头传入 requestToken。成功返回 token，响应禁止缓存。
匿名返回 401，普通账号返回 403，防伪验证失败返回 400，目标不存在返回 404。

令牌为敏感凭据，调用方负责安全交付。目标账号可通过宿主原生
UserManager.ResetPasswordAsync 消费令牌。当前交付为管理 API，
账号管理界面和面向用户的密码恢复界面需要后续产品设计。

## 已有部署验证

使用一致 SQLite 备份恢复到独立文件，配置 DefaultConnection 指向副本。
保留原 Data Protection 密钥和应用标识，核对旧账号登录、Cookie、
稳定用户 ID、业务读写、管理员允许与普通用户拒绝。
密码重置后安全戳发生变化，已有 Cookie 的失效时机遵循宿主安全戳验证配置。
