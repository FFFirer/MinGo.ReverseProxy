## Context

证书管理系统分为控制面（MinGo.ControlPlane.Api）和数据面（MinGo.DataPlane）。控制面使用SQLite存储证书（个人证书/PFX文件以字节数组形式存储），通过gRPC双向流（ConfigReplication.ReplicateConfig）向下推送给数据面。数据面将证书元数据保存在内存中，在TLS握手时通过DataPlaneCertificateSelector根据SNI域名匹配证书。

当前代码中以下组件已实现但未正确连线：

| 组件 | 状态 | 问题 |
|------|------|------|
| `DataPlaneCertificateSelector` | 已注册为Singleton | `ReloadFromProvider()` 从未被调用 |
| `ConfigureKestrelHttps()` | 方法已定义 | 未在Program.cs中调用 |
| `ConfigReplicationService.BroadcastConfigUpdateAsync()` | 已实现 | 广播不含证书数据 |
| `ApiManagementService` 证书方法 | 已实现 | 未触发ConfigUpdate通知 |
| DataPlane appsettings.json | 仅有HTTP端口 | 缺少HTTPS端点配置 |

## Goals / Non-Goals

**Goals:**
- 数据面暴露HTTPS端口，支持TLS 1.2/1.3
- 数据面SNI选择器正确接收gRPC下发的证书并加载到X509缓存
- 控制面证书CRUD操作触发gRPC广播，推送证书数据到数据面
- 数据面在收到新证书后自动重载选择器缓存，无需重启

**Non-Goals:**
- 不修改运维架构（docker-compose端口映射由外部处理）
- 不引入新的外部依赖
- 不修改gRPC proto定义
- 不处理旧项目（MinGo.ReverseProxy）的证书管理

## Decisions

### D1: HTTPS端口复用数据面进程

**选择**：在DataPlane的Kestrel配置中增加HTTPS端点，复用现有进程（不拆分为独立网关）。
**理由**：数据面本身就是YARP反向代理进程，负责请求转发。TLS卸载是反向代理的标准职责，在同一进程中完成可避免额外的网络跳转。
**备选方案**：前端负载均衡器（如nginx/haproxy）卸载TLS → 增加运维复杂度，且违背数据面设计初衷。

### D2: 在ConfigSyncService中直接注入并调用ReloadFromProvider

**选择**：将DataPlaneCertificateSelector注入到ConfigSyncService，在ApplyConfigSnapshotAsync中调用ReloadFromProvider()。
**理由**：ConfigSyncService已经是配置变更的唯一入口，在这里触发证书重载最自然、最可靠。注入方式为构造函数注入，DataPlaneCertificateSelector是Singleton。
**备选方案**：通过事件/通知机制解耦 → 增加不必要的复杂性，单接收者场景不需要事件总线。

### D3: BroadcastConfigUpdateAsync从数据库读取证书

**选择**：复用现有的`apiDbService.GetCertificatesAsync()`获取证书数据，在BuildConfigSnapshotAsync和BroadcastConfigUpdateAsync中统一使用相同逻辑。
**理由**：确保初始全量同步和增量广播的数据一致性。避免维护两个不同的证书读取路径。

### D4: 证书CRUD通知复用现有GatewayEvent机制

**选择**：在ApiManagementService的证书CRUD方法中调用`_gatewayEventService.SendEventToAllInstancesAsync(ConfigUpdate, ...)`，与路由/集群变更使用相同的通知通道。
**理由**：现有`NotifyGatewayConfigChangeAsync()`方法已被路由和集群CRUD调用，复用此方法无需新增基础设施。

## Risks / Trade-offs

| 风险 | 缓解措施 |
|------|---------|
| 证书字节在gRPC传输和内存中可能暴露密码 | 添加日志脱敏（不记录证书密码）；使用`X509KeyStorageFlags.Exportable`确保密钥可导出 |
| 证书重载期间存在短暂窗口期（旧证书已卸载、新证书未加载完成） | `ReloadFromProvider()`在lock内完成全量替换，加载期间仍使用旧缓存；加载完成后原子替换 |
| 大量证书影响数据面内存 | 单个PFX证书通常<5KB，100个证书约500KB，内存影响可忽略 |
| 证书过期后继续使用 | 依赖控制面定时检查证书有效性；数据面仅验证证书可加载，不做有效期断言 |

## Implementation Plan

### 1. 添加HTTPS端点配置

文件：`src/MinGo.DataPlane/appsettings.json`

```json
"Kestrel": {
  "Endpoints": {
    "Http": { "Url": "http://+:8080" },
    "Https": { "Url": "https://+:8443" }
  }
}
```

### 2. 在DataPlane Program.cs中注册SNI选择器到Kestrel

文件：`src/MinGo.DataPlane/Program.cs`

在`builder.Services.AddCertificateServices();`之后增加Kestrel HTTPS配置。

### 3. 连接ConfigSyncService与DataPlaneCertificateSelector

- 将`DataPlaneCertificateSelector`注入到`ConfigSyncService`
- 在`ApplyConfigSnapshotAsync()`中，调用`_certSelector.ReloadFromProvider()`

### 4. BroadcastConfigUpdateAsync包含证书

在`ConfigReplicationService.BroadcastConfigUpdateAsync()`中添加证书读取和推送逻辑。

### 5. ApiManagementService证书方法触发通知

在`ApiManagementService.CreateCertificateAsync()`、`UpdateCertificateAsync()`、`DeleteCertificateAsync()`中添加`NotifyGatewayConfigChangeAsync()`调用。

### 6. 更新docker-compose（可选）

在docker-compose.yml的数据面配置中暴露8443端口。
