## 1. 添加 NuGet 包引用

- [ ] 1.1 DataPlane.csproj 添加 `Serilog.AspNetCore` PackageReference

## 2. 创建共享 Serilog 扩展方法

- [ ] 2.1 在 MinGo.Core 中创建 `Logging/SerilogSetup.cs`
- [ ] 2.2 实现 `UseSharedSerilog(this IHostBuilder)` 扩展方法，包含环境级别覆盖

## 3. 更新 DataPlane 项目

- [ ] 3.1 DataPlane/Program.cs 添加 `builder.Host.UseSharedSerilog()` 调用
- [ ] 3.2 DataPlane/appsettings.json 添加 `Serilog` 配置节
- [ ] 3.3 DataPlane/appsettings.Development.json 添加 `Serilog` Development 覆盖

## 4. 更新 ControlPlane 项目

- [ ] 4.1 ControlPlane/Program.cs 替换内联 Serilog 初始化为 `UseSharedSerilog()`
- [ ] 4.2 ControlPlane/appsettings.json 规范化 Serilog 配置节

## 5. 验证

- [ ] 5.1 构建通过，无 LSP 诊断错误
