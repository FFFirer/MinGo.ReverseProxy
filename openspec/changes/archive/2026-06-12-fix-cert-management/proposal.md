## Why

证书管理功能在控制面→数据面的下发链路中存在三处断链，导致数据面无法正确加载和匹配证书。当前代码骨架完整（存储、gRPC传输、SNI选择器类均存在），但缺少关键的接线逻辑。修复后控制面证书变更可自动下发到数据面，数据面Kestrel可根据请求域名SNI正确选择对应证书完成TLS握手。

## What Changes

1. **数据面HTTPS端口配置**：在DataPlane的appsettings.json中增加HTTPS端点配置，使Kestrel监听TLS端口
2. **证书重载触发**：在ConfigSyncService收到证书更新后，调用DataPlaneCertificateSelector.ReloadFromProvider()将证书字节加载到X509缓存
3. **SNI选择器注册**：在DataPlane Program.cs中调用ConfigureKestrelHttps()，将ServerCertificateSelector回调注册到Kestrel
4. **证书变更通知**：在ApiManagementService的证书CRUD方法中添加NotifyGatewayConfigChangeAsync()调用，使证书变更触发gRPC广播
5. **广播含证书数据**：在ConfigReplicationService.BroadcastConfigUpdateAsync()中添加证书数据的推送

## Capabilities

### New Capabilities

- `data-plane-tls`: 数据面HTTPS/TLS端点配置与SNI证书选择
- `cert-sync-reload`: 证书下发后的自动重载机制

### Modified Capabilities

- `grpc-config-replication`: ConfigReplication gRPC广播需要包含证书数据；初始订阅已含证书但增量广播缺失

## Impact

- **src/MinGo.DataPlane/appsettings.json**：增加HTTPS端点
- **src/MinGo.DataPlane/Program.cs**：调用ConfigureKestrelHttps()
- **src/MinGo.DataPlane/ConfigSync/ConfigSyncService.cs**：注入cert selector并触发reload
- **src/MinGo.ControlPlane.Api/GrpcServices/ConfigReplicationService.cs**：广播中包含证书
- **src/MinGo.Application/Services/ApiManagementService.cs**：证书CRUD触发通知
- **docker-compose.yml**：可能需要暴露数据面HTTPS端口
