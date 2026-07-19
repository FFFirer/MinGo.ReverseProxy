## Context

当前 DataPlane 的日志走的是 `Microsoft.Extensions.Logging` 默认的 Console Provider，控制面已使用 Serilog。两个平面日志格式不一致，且 DataPlane 缺少结构化日志能力。本次设计将 Serilog 接入 DataPlane，并通过共享代码方式统一两个平面的 Serilog 配置逻辑。

## Goals / Non-Goals

**Goals:**
- DataPlane 使用 Serilog 输出结构化日志（与 ControlPlane 格式一致）
- 通过共享扩展方法统一两个平面的 Serilog 初始化逻辑
- 环境感知的日志级别（Development=Debug, Production=Warning）
- YARP 代理内部日志在 Serilog 层面按 Warning 级别过滤

**Non-Goals:**
- 不改变 TelemetryStore / 内存指标采集逻辑
- 不替换 OpenTelemetry 包（包已引用但暂不启用）
- 不添加文件日志或外部日志输出
- 不改动现有 `ILogger<T>` 使用方式

## Decisions

### 1. 共享方式：代码共享（扩展方法）而非配置文件共享

**选择**: 在 `MinGo.Core` 中创建 `Logging/SerilogSetup.cs`，包含 `UseSharedSerilog(this IHostBuilder)` 扩展方法。

**理由**: 
- 环境级别差异（Debug vs Warning）用代码表达比 JSON 配置更清晰
- 强类型，编译时检查
- 后续添加其他 Sink（如 OpenTelemetry）更方便

### 2. 配置策略：代码 + appsettings.json 双层覆盖

扩展方法中设置基线（MinimumLevel 等），具体的 WriteTo 行为由 `ReadFrom.Configuration` 从 `appsettings.json` 加载，允许每个项目按需覆盖。

```
UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration);     // 从配置文件加载
         .MinimumLevel.Override("Microsoft.AspNetCore", Warning)
         .MinimumLevel.Override("Yarp", Warning)
         .Enrich.FromLogContext()
         .Enrich.WithMachineName();
    
    // 环境级别在配置文件中通过 Serilog.MinimumLevel.Default 控制
});
```

### 3. 环境级别区分方式

通过 `appsettings.{Environment}.json` 或 `appsettings.json` 中的 `Serilog.MinimumLevel.Default` 控制：

| 环境 | Serilog.MinimumLevel.Default |
|---|---|
| Development | Debug |
| Production | Warning |

每个项目的 `appsettings.json` 中统一加 `Serilog` 节，`appsettings.Development.json` 覆盖为 Debug。

### 4. 日志格式

沿用 Serilog.AspNetCore 默认的 Console 输出格式（带颜色和结构化属性），与 ControlPlane 现有行为一致。

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| ControlPlane 现有 Serilog 初始化方式被替换，可能引入回归 | 扩展方法保持与现有行为等价（Console sink + ReadFrom.Configuration） |
| DataPlane 新增 Serilog 包引用可能遗漏 | 已在 Directory.Packages.props 中集中管理版本，DataPlane.csproj 加 PackageReference 即可 |
| YARP 代理请求日志刷屏 | 通过 `MinimumLevel.Override("Yarp", Warning)` 过滤 |
