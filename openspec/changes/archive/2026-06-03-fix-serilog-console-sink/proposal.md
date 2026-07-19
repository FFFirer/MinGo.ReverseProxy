## Why

Control Plane 使用 Serilog 作为日志框架，但 `appsettings.json` 的 Serilog 配置中未定义任何 `WriteTo` sink（如 Console），导致所有 `ILogger<T>` 日志输出被 Serilog 接收后无处可去，容器中无任何日志输出。

## What Changes

- 在 `src/MinGo.ControlPlane.Api/appsettings.json` 的 `Serilog` 配置节中新增 `WriteTo: [{ Name: "Console" }]`
- 使 Serilog 将日志写入 stdout，容器（podman/docker）可正常捕获和显示

## Capabilities

### New Capabilities
- `serilog-console-sink`: 为 Serilog 配置 Console sink，确保容器环境下日志可正常输出到 stdout

### Modified Capabilities

（无）

## Impact

- `src/MinGo.ControlPlane.Api/appsettings.json`: 仅添加 4 行配置，不影响其他环境
