## ADDED Requirements

### Requirement: Buildable targets SHALL be limited to the split control plane and data plane

The repository's build graph SHALL contain exactly the post-split projects: `MinGo.Core`, `MinGo.Application`, `MinGo.Infrastructure`, `MinGo.ControlPlane.Api`, `MinGo.DataPlane`, plus the two test projects. The pre-split monolith `src/MinGo.ReverseProxy` SHALL NOT remain in the repository, and `MinGo.ReverseProxy.slnx` SHALL NOT retain commented-out `<Project>` entries or the `TODO: 迁移完成后移除以下项目` marker for it.

#### Scenario: Solution builds after removal

- **WHEN** running `dotnet build MinGo.ReverseProxy.slnx -m:1` after the cleanup
- **THEN** the build SHALL succeed with no reference to `src/MinGo.ReverseProxy` or `src/MinGo.Shared`

#### Scenario: No dormant solution entries

- **WHEN** inspecting `MinGo.ReverseProxy.slnx`
- **THEN** every listed project SHALL be an active project
- **AND** the file SHALL NOT contain commented project entries left over from the split

### Requirement: Each domain model SHALL have exactly one definition in the repository

Domain models (`GatewayConfig`, `ProxyConfigEntities`, `CertificateConfig`, `RouteConfig`, `ClusterConfig`, and their entity counterparts) SHALL exist only under `MinGo.Core.Models` and `MinGo.Core.Entities`. The duplicated `MinGo.Shared.Models` copies SHALL be deleted, because they had already diverged from current specifications — they still carried a separate `Name` field that `cluster-name-is-id` declares SHALL NOT exist.

#### Scenario: No same-named model in a second namespace

- **WHEN** searching the source tree for `class ClusterConfig` and `class ApiClusterEntity`
- **THEN** each SHALL be found in exactly one live namespace

#### Scenario: Dead namespace is gone

- **WHEN** searching for `using MinGo.Shared.Models`
- **THEN** no match SHALL be found anywhere in `src/` or `tests/`

### Requirement: CI and local builds SHALL NOT depend on a root Dockerfile

Image builds SHALL use `ControlPlane.Dockerfile` and `DataPlane.Dockerfile`. The root `Dockerfile` that published the retired monolith (`dotnet publish src/MinGo.ReverseProxy`, `ENTRYPOINT ["dotnet","MinGo.ReverseProxy.dll"]`) SHALL be deleted, and `.github/workflows/build.yml`, `.gitea/workflows/build.yml`, `docker-compose.yml` and `docker-compose.local.yml` SHALL continue to reference only the split Dockerfiles. Documentation SHALL NOT claim that a root `Dockerfile` is required.

#### Scenario: Both images still build

- **WHEN** building with `-f ControlPlane.Dockerfile` and `-f DataPlane.Dockerfile`
- **THEN** both builds SHALL succeed after the root `Dockerfile` is removed

#### Scenario: Documentation matches the pipeline

- **WHEN** reading `docs/GITEA_ACTIONS_WORKFLOW.md`
- **THEN** it SHALL describe the split Dockerfiles
- **AND** it SHALL NOT state that a root `Dockerfile` is required

### Requirement: Types whose only registrant is removed SHALL NOT remain

Deleting the monolith removes the only registration of the legacy certificate manager (`src/MinGo.ReverseProxy/Kestrel/Extensions.cs:16-18` registered `CertificateManager`, `ICertificateManager` and `IServerCertificateSelector`). Because no live composition root registers them and `ReverseProxyExtensions` has no callers at all, this change SHALL also delete `src/MinGo.Infrastructure/Services/CertificateManager.cs`, `src/MinGo.Core/Interfaces/ICertificateManager.cs` (with `CertificateInfo`), `src/MinGo.Infrastructure/Kestrel/IServerCertificateSelector.cs` and `src/MinGo.Infrastructure/Kestrel/X509CertificateHelpers.cs`, and SHALL remove the entire `ReverseProxyExtensions` class from `src/MinGo.Infrastructure/Extensions.cs` while keeping `HttpClientExtensions` in that file.

#### Scenario: No unreachable certificate manager survives

- **WHEN** searching `src/` for `CertificateManager` and `ICertificateManager` after the cleanup
- **THEN** no match SHALL be found outside `openspec/` and `archived/`

#### Scenario: The live selector is the only one

- **WHEN** the data plane resolves a certificate for a TLS handshake
- **THEN** `MinGo.DataPlane.Kestrel.DataPlaneCertificateSelector` SHALL be the sole certificate-selection implementation present in the build graph

#### Scenario: Removal does not change runtime behaviour

- **WHEN** the control plane and data plane run after the orphan removal
- **THEN** certificate loading and TLS handshakes SHALL behave exactly as before, because nothing resolved the removed types

#### Scenario: Dead extension class is gone

- **WHEN** searching `src/` for `LoadFromDatabase`, `InitializeCertificatesAsync`, `UseDevelopmentAutoMigration`, `InitializeDatabaseProxyConfigAsync` and `ConfigureKestrelSni`
- **THEN** no match SHALL be found in `src/`
