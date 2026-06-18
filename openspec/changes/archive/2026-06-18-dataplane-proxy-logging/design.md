## Context

当前 `dev` 分支已拆分为控制面/数据面分离架构：

```
┌───────────────────┐     gRPC     ┌──────────────────┐
│ ControlPlane.Api   │◄───────────►│   DataPlane       │
│ (port 5000/5001)   │  config     │ (port 8080/8443)  │
│                    │  sync/hb    │                   │
│ - REST API + Admin │             │ - YARP Proxy      │
│ - Identity Auth    │             │ - Telemetry(指标)  │
│ - SQLite           │             │ - Config Sync     │
└───────────────────┘             └───────────────────┘
```

数据面 `Program.cs` 当前管道：
```
GatewayTelemetryMiddleware  (指标 → TelemetryStore)
        │
MapReverseProxy             (YARP 代理)
```

**无请求日志输出**。Console 仅输出 Serilog 框架级/启动日志。

Serilog.AspNetCore 已引用。`SerilogSetup` 共享配置已抑制 YARP 分类日志：
```csharp
cfg.MinimumLevel.Override("Yarp", LogEventLevel.Warning);
```

## Goals / Non-Goals

**Goals:**
- 数据面每个代理请求输出日志（Method、Path、StatusCode、Elapsed）
- 输出格式通过 `appsettings.json` 配置切换
- 代码变更不超过 1 行

**Non-Goals:**
- 不新增接口 / 中间件 / 自定义 Logger
- 不按状态码分级（统一 `Information` — Serilog 默认行为）
- 不记录请求/响应 Body 和 Headers
- 不修改 `GatewayTelemetryMiddleware`（指标与日志职责分离）

## Decisions

### 1. 日志中间件位置

**选择**: 在 `Program.cs` 的 `app.UseGatewayTelemetry()` 后添加

```csharp
app.UseGatewayTelemetry();       // 指标 → TelemetryStore
app.UseSerilogRequestLogging();  // ← 新增 1 行
app.MapReverseProxy();
```

**理由**: `UseSerilogRequestLogging` 包装下游管道，在响应完成后自动输出日志。置于 `UseGatewayTelemetry` 之后保证日志在指标采集后才输出，避免时序问题。

### 2. 日志级别

**选择**: 使用默认配置，统一 `Information` 级别

Serilog.AspNetCore 默认的 `RequestLoggingOptions.GetLevel` 行为是全部返回 `LogEventLevel.Information`。这意味着所有代理请求（正常/4xx/5xx）均以 `Information` 记录。

共享 SerilogSetup 已通过代码 `cfg.MinimumLevel.Override("Yarp", Warning)` 抑制 YARP 内部重复日志，无需配置文件额外处理。

### 3. 输出格式

**选择**: 默认纯文本，通过 `appsettings.json` 可切 JSON

```jsonc
// 纯文本（默认）
"WriteTo": [{ "Name": "Console" }]

// 切换为 JSON
"WriteTo": [{
  "Name": "Console",
  "Args": {
    "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
  }
}]
```

默认输出示例:
```
[06-18 10:30:45 INF] HTTP GET /api/users responded 200 in 45.1234 ms
```

### 4. 现有共享配置的影响

`SerilogSetup.ConfigureSharedSerilog()` 在 `DataPlane/Program.cs` 中作为 `builder.Host.UseSerilog(...)` 的参数调用。其行为：

```csharp
cfg.ReadFrom.Configuration(ctx.Configuration);   // appsettings.json → Serilog 配置
cfg.MinimumLevel.Override("Microsoft.AspNetCore", Warning);
cfg.MinimumLevel.Override("Yarp", Warning);       // 抑制 YARP 内部重复 log
cfg.Enrich.FromLogContext();
```

这意味着 YARP 分类已通过代码（非配置文件）抑制为 Warning。如果未来需要在配置文件中覆盖，`appsettings.json` 的 `Serilog.MinimumLevel.Override` 优先级更高。

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| 高流量下日志量剧增 | 单行 ~200 字节，开销约 1μs。百万级 req/s 场景可通过 `Serilog.MinimumLevel.Override("Microsoft.AspNetCore", "Error")` 抑制 |
| GatewayTelemetryMiddleware 与 UseSerilogRequestLogging 可能重复记录耗时 | TelemetryMiddleware 始终先执行采集指标，RequestLogging 在响应后输出日志。互不干扰 |
