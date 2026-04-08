# Graph Report - C:/Users/0sdf0/source/repos/MinGo.ReverseProxy  (2026-04-08)

## Corpus Check
- 146 files ¡¤ ~54,765 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 577 nodes ¡¤ 570 edges ¡¤ 79 communities detected
- Extraction: 89% EXTRACTED ¡¤ 11% INFERRED ¡¤ 0% AMBIGUOUS ¡¤ INFERRED: 62 edges (avg confidence: 0.5)
- Token cost: 0 input ¡¤ 0 output

## God Nodes (most connected - your core abstractions)
1. `ApiDbService` - 27 edges
2. `ApiManagementService` - 22 edges
3. `IApiDbService` - 20 edges
4. `IApiManagementService` - 19 edges
5. `IApiManagementService` - 19 edges
6. `EventsController` - 13 edges
7. `GatewayInstanceRegistrationService` - 12 edges
8. `ApiManagementController` - 12 edges
9. `GatewayInstanceService` - 11 edges
10. `CertificatesController` - 11 edges

## Surprising Connections (you probably didn't know these)
- `ApiManagementController` --inherits--> `ControllerBase`  [EXTRACTED]
  C:\Users\0sdf0\source\repos\MinGo.ReverseProxy\src\MinGo.ReverseProxy\Controllers\ApiManagementController.cs ¡ú   _Bridges community 1 ¡ú community 12_
- `CertificatesController` --inherits--> `ControllerBase`  [EXTRACTED]
  C:\Users\0sdf0\source\repos\MinGo.ReverseProxy\src\MinGo.ReverseProxy\Controllers\CertificatesController.cs ¡ú   _Bridges community 1 ¡ú community 14_
- `InstancesController` --inherits--> `ControllerBase`  [EXTRACTED]
  C:\Users\0sdf0\source\repos\MinGo.ReverseProxy\src\MinGo.ReverseProxy\Controllers\InstancesController.cs ¡ú   _Bridges community 1 ¡ú community 18_
- `MonitoringController` --inherits--> `ControllerBase`  [EXTRACTED]
  C:\Users\0sdf0\source\repos\MinGo.ReverseProxy\src\MinGo.ReverseProxy\Controllers\MonitoringController.cs ¡ú   _Bridges community 1 ¡ú community 19_
- `ApiDbService` --inherits--> `IApiDbService`  [EXTRACTED]
  C:\Users\0sdf0\source\repos\MinGo.ReverseProxy\src\MinGo.Infrastructure\Data\ApiDbService.cs ¡ú   _Bridges community 5 ¡ú community 3_

## Communities

### Community 0 - "Service Interfaces"
Cohesion: 0.04
Nodes (6): IApiManagementService, IGatewayEventSender, IGatewayEventService, IGatewayInstanceService, ILogService, IMonitoringService

### Community 1 - "API Controllers"
Cohesion: 0.07
Nodes (8): ConfigController, ControllerBase, EventsController, LogsController, MinGo.ReverseProxy.Controllers, ActivityExtensions, MinGo.ReverseProxy.Controllers, TelemetryController

### Community 2 - "Community 2"
Cohesion: 0.07
Nodes (5): IGatewayEventSender, IGatewayEventService, IGatewayInstanceService, ILogService, IMonitoringService

### Community 3 - "Database Service"
Cohesion: 0.13
Nodes (1): ApiDbService

### Community 4 - "Management Service"
Cohesion: 0.12
Nodes (2): ApiManagementService, IApiManagementService

### Community 5 - "Community 5"
Cohesion: 0.1
Nodes (2): IApiDbService, IApiDbService

### Community 6 - "Config Provider"
Cohesion: 0.12
Nodes (8): ConfigUpdateEventListener, ControlPlaneConfigResponse, DatabaseProxyConfig, DatabaseProxyConfigProvider, IDisposable, IHostedService, IProxyConfig, IProxyConfigProvider

### Community 7 - "Community 7"
Cohesion: 0.11
Nodes (1): IApiManagementService

### Community 8 - "Config Models"
Cohesion: 0.11
Nodes (17): ApiKeyConfig, CertificateConfig, ClusterConfig, DestinationConfig, GatewayConfig, HealthCheckActiveConfig, HealthCheckConfig, HealthCheckPassiveConfig (+9 more)

### Community 9 - "Community 9"
Cohesion: 0.19
Nodes (3): BackgroundService, GatewayInstanceHealthCheckService, GatewayInstanceRegistrationService

### Community 10 - "Community 10"
Cohesion: 0.12
Nodes (3): IMessageNotificationService, MemoryMessageNotificationService, Subscription

### Community 11 - "Community 11"
Cohesion: 0.17
Nodes (5): GatewayEventSender, GatewayEventService, HttpGatewayEventSender, IGatewayEventSender, IGatewayEventService

### Community 12 - "Community 12"
Cohesion: 0.15
Nodes (2): ApiManagementController, MinGo.ReverseProxy.Controllers

### Community 13 - "Community 13"
Cohesion: 0.2
Nodes (2): GatewayInstanceService, IGatewayInstanceService

### Community 14 - "Certificates Controller"
Cohesion: 0.2
Nodes (2): CertificatesController, MinGo.ReverseProxy.Controllers

### Community 15 - "Community 15"
Cohesion: 0.24
Nodes (2): MinGo.ReverseProxy.Shared, Sidebar

### Community 16 - "Community 16"
Cohesion: 0.27
Nodes (5): IEntityTypeConfiguration, ApiCertificateEntityConfiguration, ApiClusterEntityConfiguration, ApiDestinationEntityConfiguration, ApiRouteEntityConfiguration

### Community 17 - "Community 17"
Cohesion: 0.25
Nodes (2): IMonitoringService, MonitoringService

### Community 18 - "Community 18"
Cohesion: 0.22
Nodes (2): InstancesController, MinGo.ReverseProxy.Controllers

### Community 19 - "Community 19"
Cohesion: 0.22
Nodes (2): MinGo.ReverseProxy.Controllers, MonitoringController

### Community 20 - "Proxy Entities"
Cohesion: 0.25
Nodes (4): ApiCertificateEntity, ApiClusterEntity, ApiDestinationEntity, ApiRouteEntity

### Community 21 - "Community 21"
Cohesion: 0.29
Nodes (2): ILogService, LogService

### Community 22 - "Community 22"
Cohesion: 0.29
Nodes (3): MetricPoint, MinGo.Core.Services, TelemetryStore

### Community 23 - "Program Entry"
Cohesion: 0.29
Nodes (2): HttpClientExtensions, ReverseProxyExtensions

### Community 24 - "Community 24"
Cohesion: 0.29
Nodes (2): Header, MinGo.ReverseProxy.Shared

### Community 25 - "Community 25"
Cohesion: 0.29
Nodes (3): LayoutComponentBase, MainLayout, MinGo.ReverseProxy.Shared

### Community 26 - "Community 26"
Cohesion: 0.29
Nodes (3): ApiDbContext, DbContext, ProxyDbContext

### Community 27 - "Community 27"
Cohesion: 0.33
Nodes (5): GatewayInstance, GatewayInstanceHeartbeatRequest, GatewayInstanceListResponse, GatewayInstanceRegisterRequest, GatewayInstanceResponse

### Community 28 - "Community 28"
Cohesion: 0.33
Nodes (5): AccessLog, ErrorLog, LogQuery, LogStatistics, LogTrend

### Community 29 - "Community 29"
Cohesion: 0.33
Nodes (5): ErrorMetrics, MetricsSummary, RequestMetrics, ServiceMetrics, SystemMetrics

### Community 30 - "Telemetry Middleware"
Cohesion: 0.33
Nodes (3): GatewayTelemetryMiddleware, GatewayTelemetryMiddlewareExtensions, MinGo.Infrastructure.ExternalServices

### Community 31 - "Community 31"
Cohesion: 0.33
Nodes (3): Init, MinGo.Infrastructure.Migrations, Migration

### Community 32 - "Community 32"
Cohesion: 0.4
Nodes (3): ApiDbContextModelSnapshot, MinGo.Infrastructure.Migrations, ModelSnapshot

### Community 33 - "Community 33"
Cohesion: 0.4
Nodes (3): ErrorModel, MinGo.ReverseProxy.Pages, PageModel

### Community 34 - "Community 34"
Cohesion: 0.5
Nodes (3): GatewayEvent, GatewayEventResponse, GatewayEventSubscriptionRequest

### Community 35 - "Community 35"
Cohesion: 0.5
Nodes (2): ApiDbContextDesignTimeFactory, IDesignTimeDbContextFactory

### Community 36 - "Community 36"
Cohesion: 0.5
Nodes (2): Init, MinGo.Infrastructure.Migrations

### Community 37 - "Community 37"
Cohesion: 0.5
Nodes (2): App, MinGo.ReverseProxy

### Community 38 - "Community 38"
Cohesion: 0.5
Nodes (2): Certificates, MinGo.ReverseProxy.Pages

### Community 39 - "Community 39"
Cohesion: 0.5
Nodes (2): Clusters, MinGo.ReverseProxy.Pages

### Community 40 - "Community 40"
Cohesion: 0.5
Nodes (2): Dashboard, MinGo.ReverseProxy.Pages

### Community 41 - "Community 41"
Cohesion: 0.5
Nodes (2): AspNetCoreGeneratedDocument, Pages_Error

### Community 42 - "Community 42"
Cohesion: 0.5
Nodes (2): Instances, MinGo.ReverseProxy.Pages

### Community 43 - "Community 43"
Cohesion: 0.5
Nodes (2): Logs, MinGo.ReverseProxy.Pages

### Community 44 - "Community 44"
Cohesion: 0.5
Nodes (2): MinGo.ReverseProxy.Pages, Monitoring

### Community 45 - "Community 45"
Cohesion: 0.5
Nodes (2): MinGo.ReverseProxy.Pages, Routes

### Community 46 - "Community 46"
Cohesion: 0.5
Nodes (2): MinGo.ReverseProxy.Pages, Security

### Community 47 - "Community 47"
Cohesion: 0.5
Nodes (2): MinGo.ReverseProxy.Pages, Settings

### Community 48 - "Community 48"
Cohesion: 0.5
Nodes (2): MinGo.ReverseProxy.Pages, Pages__Host

### Community 49 - "Community 49"
Cohesion: 0.5
Nodes (2): MinGo.ReverseProxy.Pages, Pages__Layout

### Community 50 - "Community 50"
Cohesion: 0.5
Nodes (2): MinGo.ReverseProxy.Shared, PageHeader

### Community 51 - "Community 51"
Cohesion: 0.5
Nodes (2): _Imports, MinGo.ReverseProxy

### Community 52 - "Community 52"
Cohesion: 0.67
Nodes (1): ControlPlaneOptions

### Community 53 - "Community 53"
Cohesion: 0.67
Nodes (1): ReverseProxyExtensions

### Community 54 - "Community 54"
Cohesion: 0.67
Nodes (2): EmbeddedAttribute, Microsoft.CodeAnalysis

### Community 55 - "Community 55"
Cohesion: 0.67
Nodes (2): Microsoft.Extensions.Validation.Embedded, ValidatableTypeAttribute

### Community 56 - "Community 56"
Cohesion: 0.67
Nodes (0): 

### Community 57 - "Community 57"
Cohesion: 0.67
Nodes (0): 

### Community 58 - "Community 58"
Cohesion: 1.0
Nodes (0): 

### Community 59 - "Community 59"
Cohesion: 1.0
Nodes (0): 

### Community 60 - "Community 60"
Cohesion: 1.0
Nodes (0): 

### Community 61 - "Community 61"
Cohesion: 1.0
Nodes (0): 

### Community 62 - "Community 62"
Cohesion: 1.0
Nodes (0): 

### Community 63 - "Community 63"
Cohesion: 1.0
Nodes (0): 

### Community 64 - "Community 64"
Cohesion: 1.0
Nodes (0): 

### Community 65 - "Community 65"
Cohesion: 1.0
Nodes (0): 

### Community 66 - "Community 66"
Cohesion: 1.0
Nodes (0): 

### Community 67 - "Community 67"
Cohesion: 1.0
Nodes (0): 

### Community 68 - "Community 68"
Cohesion: 1.0
Nodes (0): 

### Community 69 - "Community 69"
Cohesion: 1.0
Nodes (0): 

### Community 70 - "Community 70"
Cohesion: 1.0
Nodes (0): 

### Community 71 - "Community 71"
Cohesion: 1.0
Nodes (0): 

### Community 72 - "Community 72"
Cohesion: 1.0
Nodes (0): 

### Community 73 - "Community 73"
Cohesion: 1.0
Nodes (0): 

### Community 74 - "Community 74"
Cohesion: 1.0
Nodes (0): 

### Community 75 - "Community 75"
Cohesion: 1.0
Nodes (0): 

### Community 76 - "Community 76"
Cohesion: 1.0
Nodes (0): 

### Community 77 - "Community 77"
Cohesion: 1.0
Nodes (0): 

### Community 78 - "Community 78"
Cohesion: 1.0
Nodes (0): 

## Knowledge Gaps
- **74 isolated node(s):** `ControlPlaneConfigResponse`, `Subscription`, `Microsoft.CodeAnalysis`, `EmbeddedAttribute`, `Microsoft.Extensions.Validation.Embedded` (+69 more)
  These have ¡Ü1 connection - possible missing edges or undocumented components.
- **Thin community `Community 58`** (2 nodes): `counter.ts`, `setupCounter()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 59`** (1 nodes): `main.ts`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 60`** (1 nodes): `theme.js`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 61`** (1 nodes): `MinGo.Gateway.AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 62`** (1 nodes): `MinGo.Gateway.GlobalUsings.g.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 63`** (1 nodes): `MinGo.Application.AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 64`** (1 nodes): `MinGo.Application.GlobalUsings.g.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 65`** (1 nodes): `MinGo.ControlPlane.AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 66`** (1 nodes): `MinGo.ControlPlane.GlobalUsings.g.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 67`** (1 nodes): `MinGo.ControlPlane.RazorAssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 68`** (1 nodes): `MinGo.Core.AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 69`** (1 nodes): `MinGo.Core.GlobalUsings.g.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 70`** (1 nodes): `MinGo.Infrastructure.AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 71`** (1 nodes): `MinGo.Infrastructure.GlobalUsings.g.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 72`** (1 nodes): `vite.config.ts`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 73`** (1 nodes): `MinGo.ReverseProxy.AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 74`** (1 nodes): `MinGo.ReverseProxy.GlobalUsings.g.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 75`** (1 nodes): `MinGo.ReverseProxy.MvcApplicationPartsAssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 76`** (1 nodes): `MinGo.ReverseProxy.RazorAssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 77`** (1 nodes): `MinGo.Shared.AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 78`** (1 nodes): `MinGo.Shared.GlobalUsings.g.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ApiManagementController` connect `Community 12` to `API Controllers`?**
  _High betweenness centrality (0.005) - this node is a cross-community bridge._
- **Why does `ApiDbService` connect `Database Service` to `Community 5`?**
  _High betweenness centrality (0.005) - this node is a cross-community bridge._
- **What connects `ControlPlaneConfigResponse`, `Subscription`, `Microsoft.CodeAnalysis` to the rest of the system?**
  _74 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Service Interfaces` be split into smaller, more focused modules?**
  _Cohesion score 0.04 - nodes in this community are weakly interconnected._
- **Should `API Controllers` be split into smaller, more focused modules?**
  _Cohesion score 0.07 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.07 - nodes in this community are weakly interconnected._
- **Should `Database Service` be split into smaller, more focused modules?**
  _Cohesion score 0.13 - nodes in this community are weakly interconnected._