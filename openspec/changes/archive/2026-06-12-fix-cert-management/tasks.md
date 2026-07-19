## 1. 数据面HTTPS端点配置

- [x] 1.1 在 `src/MinGo.DataPlane/appsettings.json` 的Kestrel配置中添加HTTPS端点（端口8443），并配置TLS 1.2/1.3
- [x] 1.2 更新 `docker-compose.yml` 暴露数据面8443端口（`"8443:8443"`）

## 2. SNI选择器注册到Kestrel

- [x] 2.1 在 `KestrelCertificateExtensions.AddCertificateServices()` 中注册了 `IConfigureOptions<KestrelServerOptions>`，通过 DI 捕获 `DataPlaneCertificateSelector` 实例并挂载 `ServerCertificateSelector` 回调（原 `ConfigureKestrelHttps()` 使用 `context.Features.Get<>()` 方式无法工作，已改为闭包捕获的正确做法）
- [x] 2.2 验证 `ServerCertificateSelector` 回调通过闭包捕获 selector 实例，而非从连接特性中获取

## 3. 证书重载触发连线

- [x] 3.1 将 `DataPlaneCertificateSelector` 注入到 `ConfigSyncService`（构造函数参数）
- [x] 3.2 在 `ConfigSyncService.ApplyConfigSnapshotAsync()` 方法中，在 `_configProvider.UpdateCertificates(snapshot.Certificates)` 之后调用 `_certSelector.ReloadFromProvider()`

## 4. 证书变更通知

- [x] 4.1 在 `ApiManagementService.CreateCertificateAsync()` 中添加 `NotifyGatewayConfigChangeAsync()` 调用
- [x] 4.2 在 `ApiManagementService.UpdateCertificateAsync()` 中添加 `NotifyGatewayConfigChangeAsync()` 调用
- [x] 4.3 在 `ApiManagementService.DeleteCertificateAsync()` 中添加 `NotifyGatewayConfigChangeAsync()` 调用

## 5. 广播包含证书数据

- [x] 5.1 修改 `ConfigReplicationService.BroadcastConfigUpdateAsync()`，通过 `AddCertificatesToSnapshotAsync()` 添加证书到广播快照
- [x] 5.2 提取 `AddCertificatesToSnapshotAsync()` 私有方法，与 `BuildConfigSnapshotAsync` 复用证书构建逻辑

## 6. 验证

- [x] 6.1 构建验证：两个项目均编译通过，0错误0警告
- [ ] 6.2 启动控制面和数据面，验证gRPC连接成功后数据面加载证书到缓存（需手动运行 docker-compose 或 dotnet run）
- [ ] 6.3 使用 `openssl s_client -connect localhost:8443 -servername api.example.com` 验证SNI证书选择（需先通过控制面上传证书）
