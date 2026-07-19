## Why

ControlPlane.Api 项目缺少 `launchSettings.json`，无法直接用 `dotnet run` 或从 IDE 启动。每次运行时需要手动指定端口和环境变量。

## What Changes

1. 新增 `Properties/launchSettings.json` — 包含 Development 启动配置

## Capabilities

### New Capabilities

### Modified Capabilities

## Impact

- `src/MinGo.ControlPlane.Api/Properties/launchSettings.json` — 新增文件
