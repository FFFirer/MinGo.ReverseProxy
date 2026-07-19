## Why

Data Plane 当前在 `WaitForInitialConfigAsync(60s)` 阻塞等待 ControlPlane 首次配置推送。如果配置加载失败（gRPC 连接失败、ConfigSnapshot 包含无效配置项），`MapReverseProxy()` 会因 YARP 内部 ConfigValidator 抛出 `InvalidOperationException`/`AggregateException` 而导致进程崩溃。

需要让 Data Plane 能够：(1) 启动时不阻塞等待配置，(2) 配置无效时丢弃并保持旧配置而非崩溃，(3) 通过健康检查暴露配置状态。

## What Changes

- **移除 WaitForInitialConfigAsync 阻塞**：DataPlane 启动后立即开始服务，配置异步到达后热加载
- **新增配置预检 (ConfigValidator)**：在 `DataPlaneConfigProvider.ApplyConfig()` 中校验 ConfigSnapshot 合法性，校验失败时丢弃 snapshot、保持旧配置、记录 Warning 日志
- **新增健康检查端点 `/healthz/ready`**：反映配置加载状态，供负载均衡器判断节点可用性
- **MapReverseProxy 容错**：确保空初始配置不会触发 YARP 验证异常

## Capabilities

### New Capabilities

- `config-prevalidation`: DataPlane 收到 ConfigSnapshot 后、应用到 YARP 前的预检能力，校验 Route/Cluster/Destination 合法性，失败时保持旧配置

### Modified Capabilities

- `grpc-config-replication`: DataPlane 不再阻塞等待首次配置，启动即服务；配置校验失败时不应用、不崩溃

## Impact

- `src/MinGo.DataPlane/Program.cs` — 移除 WaitForInitialConfigAsync，新增 /healthz/ready 端点
- `src/MinGo.DataPlane/ConfigSync/DataPlaneConfigProvider.cs` — ApplyConfig 内集成预检逻辑
- `src/MinGo.DataPlane/ConfigSync/ConfigSyncService.cs` — 无需修改（已有重试和错误处理）
- `src/MinGo.DataPlane/MinGo.DataPlane.csproj` — 可能新增健康检查 NuGet 包（如需要）
- YARP 配置加载行为变化：从"启动时阻塞验证"变为"启动后异步验证"
