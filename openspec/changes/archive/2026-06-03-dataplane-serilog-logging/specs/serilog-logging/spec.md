## ADDED Requirements

### Requirement: DataPlane 使用 Serilog 结构化日志
DataPlane 所有 `ILogger<T>` 输出必须通过 Serilog 管道，与控制面保持一致的日志格式。

#### Scenario: DataPlane 启动时初始化 Serilog
- **WHEN** DataPlane 进程启动
- **THEN** `Log.Logger` 被创建且 `IHostBuilder.UseSerilog()` 被调用

#### Scenario: 通过 `UseSharedSerilog` 扩展方法初始化
- **WHEN** 调用 `builder.Host.UseSharedSerilog()`
- **THEN** Serilog 从 `IConfiguration` 读取配置并应用环境级别覆盖

### Requirement: 环境感知的日志级别
不同环境使用不同的默认日志级别。

#### Scenario: Development 环境输出 Debug 级别
- **WHEN** 环境为 `Development`
- **THEN** Serilog Console 输出级别为 Debug

#### Scenario: Production 环境输出 Warning 级别
- **WHEN** 环境为 `Production`
- **THEN** Serilog Console 输出级别为 Warning

### Requirement: YARP 代理日志按 Warning 过滤
YARP 反向代理的内部诊断日志不刷屏。

#### Scenario: YARP Debug/Information 日志被抑制
- **WHEN** YARP 组件记录 Debug 或 Information 级别日志
- **THEN** Serilog 不输出到 Console

### Requirement: ControlPlane 也使用共享扩展方法
ControlPlane 的 Serilog 初始化替换为 `UseSharedSerilog`。

#### Scenario: ControlPlane 启动时调用 UseSharedSerilog
- **WHEN** ControlPlane 进程启动
- **THEN** 使用 `UseSharedSerilog()` 替代内联的 Serilog 初始化代码

### Requirement: 配置通过 appsettings.json 控制
Serilog 的详细配置（WriteTo、Override 等）通过 JSON 配置文件加载。

#### Scenario: Serilog 配置从 appsettings.json 加载
- **WHEN** Serilog 初始化
- **THEN** `ReadFrom.Configuration(ctx.Configuration)` 被调用
