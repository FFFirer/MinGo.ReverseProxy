## Why

ControlPlane 已使用 Serilog 进行结构化日志输出，但 DataPlane 仍使用默认的 .NET Console Logger Provider，日志格式不统一，缺乏结构化能力。统一为 Serilog 后，两个平面日志格式一致，便于集中采集和分析。

## What Changes

- DataPlane 项目引入 `Serilog.AspNetCore` 包
- 在 `MinGo.Core` 中创建共享的 Serilog 初始化扩展方法，两个平面统一调用
- DataPlane `Program.cs` 接入 Serilog
- ControlPlane `Program.cs` 改为使用共享扩展方法（替换现有内联 Serilog 初始化）
- DataPlane `appsettings.json` 增加 `Serilog` 配置节
- ControlPlane `appsettings.json` 的 `Serilog` 配置节规范化

## Capabilities

### New Capabilities
- `serilog-logging`: 统一的 Serilog 结构化日志配置，支持环境级别区分（Development=Debug, Production=Warning），两个平面共享同一套配置逻辑

### Modified Capabilities

无。纯基础设施变更，不涉及业务能力变化。

## Impact

| 项目 | 影响 |
|---|---|
| `MinGo.DataPlane.csproj` | 新增 `Serilog.AspNetCore` 包引用 |
| `MinGo.Core` | 新增 `Logging/SerilogSetup.cs` 共享扩展方法 |
| `MinGo.DataPlane/Program.cs` | 添加 `UseSerilog()` 调用 |
| `MinGo.ControlPlane.Api/Program.cs` | 替换为共享扩展方法 |
| `MinGo.DataPlane/appsettings.json` | 新增 `Serilog` 配置节 |
| `MinGo.ControlPlane.Api/appsettings.json` | 调整 `Serilog` 配置节 |
