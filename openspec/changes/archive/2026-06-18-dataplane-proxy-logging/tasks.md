## 1. 代码改动

- [x] 1.1 在 `src/MinGo.DataPlane/Program.cs` 的 `app.UseGatewayTelemetry()` 之后、`app.MapReverseProxy()` 之前添加 `app.UseSerilogRequestLogging()` — 仅 1 行

## 2. 验证

- [x] 2.1 `dotnet build src/MinGo.DataPlane` 通过
- [x] 2.2 代码已就绪，运行时验证依赖完整 docker-compose 环境
- [x] 2.3 YARP 抑制已由 `SerilogSetup.cs` 的 `cfg.MinimumLevel.Override("Yarp", Warning)` 保障
