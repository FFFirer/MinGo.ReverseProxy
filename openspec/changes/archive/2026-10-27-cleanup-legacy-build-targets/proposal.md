## Why

`2026-06-03-split-data-control-plane` 把单体拆成了 ControlPlane + DataPlane，但拆分留下的尸体没有清干净。`.slnx` 里躺着一条 TODO：

```xml
<!-- TODO: 迁移完成后移除以下项目 -->
<!-- <Project Path="src/MinGo.ReverseProxy/MinGo.ReverseProxy.csproj" /> -->
<!-- <Project Path="src/MinGo.Shared/MinGo.Shared.csproj" /> -->
```

两个项目已从解决方案里注释掉，但文件还在磁盘上，而且根目录那个 88 行的 `Dockerfile` 仍在构建这个已退役的单体（`COPY src/MinGo.ReverseProxy .`、`dotnet publish src/MinGo.ReverseProxy`、`ENTRYPOINT ["dotnet","MinGo.ReverseProxy.dll"]`）。

危害不是"占地方"，而是**同名异味的死副本会让人改错文件**。本轮探索中真实发生过一次：读 `ProxyConfigEntities.cs` 时取到的是 `MinGo.Shared` 那份（`MinGo.Shared.Models` 全仓 0 处引用，活的模型是 `MinGo.Core.Models`，19 处引用），而两份已经漂移——死副本仍带 `ClusterConfig.Name` / `ApiClusterEntity.Name`，`cluster-name-is-id` 规格明确写着 `Name` SHALL NOT 单独存在。也就是说死副本是那次改名之前的化石，谁读到它就会得出"活代码丢了字段"的错误结论。

## What Changes

- **删除 `src/MinGo.Shared/`**（7 个被跟踪文件）：整套模型在 `MinGo.Core.Models` / `MinGo.Core.Entities` 有对应活版本，`MinGo.Shared` 只被旧单体 csproj 引用
- **删除 `src/MinGo.ReverseProxy/`**（28 个被跟踪文件：6 个旧 Controller、`Kestrel/`、`Services/AuthService.cs`、旧 vite 前端配置与 `wwwroot` 资产）：功能已由 `MinGo.ControlPlane.Api` + `frontend/min-go-console` 承接
- **删除根 `Dockerfile`**：它构建的是上面这个已退役单体；两个 CI（`.github/workflows/build.yml`、`.gitea/workflows/build.yml`）都只用 `ControlPlane.Dockerfile` / `DataPlane.Dockerfile`
- **落实 `.slnx` 里的 TODO**：删掉这两行注释掉的 `<Project>` 与 TODO 注释本身
- **删除随单体一起失去唯一注册者的证书孤儿**：`CertificateManager` 只在 `src/MinGo.ReverseProxy/Kestrel/Extensions.cs:16-18` 注册（`AddSingleton<CertificateManager>()` / `ICertificateManager` / `IServerCertificateSelector`），控制面 DI（`Program.cs:30-126`）不注册它，`InitializeCertificatesAsync` 全仓零调用者。删掉单体后这一簇成为彻底不可达的孤儿，必须同批删除，否则仓库里留着一个"看起来是唯一证书实现"的死类，且与活的数据面选择器 `DataPlaneCertificateSelector` 职责同名异味。范围（全部已核实零活引用）：`MinGo.Infrastructure/Services/CertificateManager.cs`、`MinGo.Core/Interfaces/ICertificateManager.cs`（含 `CertificateInfo`）、`MinGo.Infrastructure/Kestrel/IServerCertificateSelector.cs`、`MinGo.Infrastructure/Kestrel/X509CertificateHelpers.cs`、`MinGo.Infrastructure/Extensions.cs` 的 `ReverseProxyExtensions` 整类（5 个方法 `LoadFromDatabase` / `InitializeCertificatesAsync` / `UseDevelopmentAutoMigration` / `InitializeDatabaseProxyConfigAsync` / `ConfigureKestrelSni` 逐条核实调用者为 0）
- **修正 `docs/GITEA_ACTIONS_WORKFLOW.md`** 里"项目根目录需要存在有效的 `Dockerfile`"的表述——该要求实际不成立（两个工作流都显式指定了拆分后的 Dockerfile）

零行为变更：不新增、不修改任何运行时代码，只删除不可达的构建入口、死副本与失去注册者的孤儿类。

## Capabilities

### New Capabilities

- `build-targets`: 仓库的构建目标集合 SHALL 只包含拆分后的 ControlPlane 与 DataPlane；已退役的单体项目、其专属模型副本与其构建入口 SHALL NOT 保留在仓库中

### Modified Capabilities

（无。删除项不被任何活代码引用。）

## Impact

- **删除目录**：`src/MinGo.Shared/`、`src/MinGo.ReverseProxy/`（含 `node_modules/`、`gateway.db`、`logs/` 等本地垃圾，其中后三者未被 git 跟踪）
- **删除文件**：根 `Dockerfile`、`src/MinGo.Infrastructure/Services/CertificateManager.cs`、`src/MinGo.Core/Interfaces/ICertificateManager.cs`、`src/MinGo.Infrastructure/Kestrel/IServerCertificateSelector.cs`、`src/MinGo.Infrastructure/Kestrel/X509CertificateHelpers.cs`（该目录随之为空）
- **修改文件**：`MinGo.ReverseProxy.slnx`（移除两行注释与 TODO）、`docs/GITEA_ACTIONS_WORKFLOW.md`、`src/MinGo.Infrastructure/Extensions.cs`（删 `ReverseProxyExtensions` 整类，保留 `HttpClientExtensions`）
- **构建面**：`dotnet build MinGo.ReverseProxy.slnx` 与两个 CI 镜像构建不受影响（本来就不含这两个项目）
- **git 历史**：文件从工作树删除即达成目的；历史里的大文件（字体、`node_modules` 快照）不处理，`git filter-repo` 之类重写历史的操作明确排除在外

## Out of scope

- **不碰 `dataplane.proto` 的 `ClusterConfig.name`**：该字段（`name = 2`）在活映射里从不赋值——`ConfigReplicationService.cs:129-134` 与 `:219-223` 只设 `Id`/`LoadBalancingPolicy`/`HealthCheckJson`，所以线上一直是空串。删除 proto 字段属于 wire 兼容性变更，与本 change 的"删不可达文件"风险等级不同，留给后续
- **不重命名遗留标识符**：`ControlPlane.Dockerfile` 内的 `mingo-controlplane`、`SerilogSetup.ConfigureSharedSerilog()`、根目录名 `MinGo.ReverseProxy` 本身仍带旧项目名，属于纯改名工作
- **不动 `archived/MinGo.ControlPlane/`**：`archived/` 目录是刻意保留的历史归档，与本 change 针对的"看起来还活着的死代码"性质不同
- **不做任何行为/功能改动**（HTTPS 重定向、证书有效性、HSTS 各归自己的 change）
- **不动拆分之前就已不可达的旧配置链路**：`DatabaseProxyConfigProvider`（`src/MinGo.Infrastructure/ExternalServices/`）与 `ConfigUpdateEventListener`（同目录，实现了 `IHostedService` 但全仓没有 `AddHostedService` 注册）。删掉 `ReverseProxyExtensions` 后，它们只剩彼此这一个引用，形成一组自洽的死对；但事件订阅链路（控制面 `ConfigUpdateGrpcBroadcaster` 走的是另一条路径）值得单独确认一次再删，避免把"本 change 零行为变更"的承诺变得难以验证。同样排除：`HttpClientExtensions.AddNamedHttpClients`（零调用者，但属 HTTP 客户端配置而非拆分残留）
