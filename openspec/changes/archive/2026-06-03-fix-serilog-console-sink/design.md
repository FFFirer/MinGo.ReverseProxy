## Context

当前 `appsettings.json` 中 Serilog 配置只有 `MinimumLevel`，没有被配置任何输出 sink。`Program.cs` 中 `ReadFrom.Configuration(builder.Configuration)` 读取此配置后创建的 Logger 实例没有绑定任何输出目标。

`Serilog.AspNetCore` v9.0.0 已包含 Console sink（`Serilog.Sinks.Console`），只需在配置中声明即可。

## Goals / Non-Goals

**Goals:**
- 为 Control Plane 的 Serilog 配置添加 Console sink
- 容器 (docker/podman) 环境下日志正常输出到 stdout

**Non-Goals:**
- 不影响 Data Plane（Data Plane 不使用 Serilog）
- 不改变日志级别或格式
- 不添加文件日志或其他 sink

## Decisions

### 1. 配置方式：appsettings.json vs Program.cs

| 方式 | 说明 | 结论 |
|------|------|------|
| appsettings.json 增加 WriteTo | 声明式，和环境无关 | ✅ **选择** |
| Program.cs 加 `.WriteTo.Console()` | 代码式，硬编码 | ❌ 与环境配置分离 |

**原因**: 配置方式与环境解耦，Production/Development 可分别配置不同 sink。

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| 增加 Console sink 可能产生大量日志 | 日志级别保持 `Information`，ASP.NET Core 相关保持 `Warning` |
