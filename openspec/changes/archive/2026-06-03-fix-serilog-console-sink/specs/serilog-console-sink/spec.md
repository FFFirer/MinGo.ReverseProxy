## ADDED Requirements

### Requirement: Serilog 配置包含 Console sink
Serilog 配置 SHALL 包含 `WriteTo: [{ Name: "Console" }]` 以输出日志到 stdout。

#### Scenario: 容器启动时输出日志
- **WHEN** Control Plane 容器通过 `podman compose up` 启动
- **THEN** 控制台显示 `"Starting MinGo Control Plane..."`（Console.WriteLine）
- **THEN** 控制台显示 `"Config update gRPC broadcaster starting..."`（ILogger 输出）
- **THEN** 控制台显示 `"Data plane xxxx subscribed"` 等运行时日志

#### Scenario: 日志级别不受影响
- **WHEN** 应用正常运行
- **THEN** `Information` 级别以上的日志正常输出
- **THEN** `Microsoft.AspNetCore` 命名空间下仅 `Warning` 及以上级别输出
