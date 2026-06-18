## Why

数据面（MinGo.DataPlane）是独立的反向代理服务，当前管道中缺少请求日志中间件，代理请求的 Method、Path、StatusCode、耗时等元数据不输出到 Console/日志系统，线上排查问题需要临时改动部署。

## What Changes

- **启用 Serilog 请求日志**：在数据面管道添加 `UseSerilogRequestLogging()`（代码改动 1 行）
- **输出格式通过配置控制**：Serilog.WriteTo formatter 配置可切换纯文本 / JSON
- **YARP 分类日志级别已在共享配置中处理**：`SerilogSetup.cs` 已有 `Yarp → Warning`，无需重复
- **最小改动**：不新增接口 / 中间件 / 自定义 Logger，不修改现有 `GatewayTelemetryMiddleware`

## Capabilities

### New Capabilities
- `proxy-request-logging`: 数据面对外请求的日志记录能力，覆盖日志级别管理、输出格式配置

### Modified Capabilities

无

## Impact

- `src/MinGo.DataPlane/Program.cs` — 新增 `app.UseSerilogRequestLogging()` 1 行
- `src/MinGo.DataPlane/appsettings.json` — 可选调整格式配置（纯文本 / JSON）
- 依赖: `Serilog.AspNetCore`（已引用，无需新增包）
- 无第三方依赖变更
