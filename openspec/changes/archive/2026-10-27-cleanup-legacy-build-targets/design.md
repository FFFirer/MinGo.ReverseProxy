## 1. Context

`2026-06-03-split-data-control-plane` 之后，仓库同时存在"拆分后的活代码"和"拆分前的单体 + 它的模型副本"。本 change 只做删除，把 `.slnx` 里那条 TODO 履行掉。

设计取向是**删除而不是继续留着**，理由是本 change 的动机不是省磁盘，而是消除"能搜到但不可达"的误导源。

## 2. Goals / Non-Goals

**Goals**

- 仓库里不再存在 `MinGo.Shared.Models` 与 `MinGo.Core.Models` 的同名双份
- 不再存在构建已退役单体的根 `Dockerfile`
- `dotnet build` 图、两个 CI、compose 行为完全不变
- 修正文档里"根目录必须有 Dockerfile"这条不成立的要求

**Non-Goals**

- 不改任何运行时行为、不碰 proto、不重命名遗留标识符
- 不重写 git 历史（大文件留在历史里）
- 不动 `archived/` 目录（见 3.2 的取舍）
- 不处理 `src/MinGo.ControlPlane`、`src/MinGo.Gateway` 这两个空壳目录——它们被跟踪文件数为 0，只是本地 `bin/obj/node_modules` 残留，`rm -rf` 即可，不构成仓库变更

## 3. Decisions

### 3.1 判定"死"的证据链，逐条可复核

| 对象 | 证据 |
|---|---|
| `src/MinGo.Shared` | `using MinGo.Shared.Models` 全仓 **0** 处（对照 `MinGo.Core.Models` 19 处）；唯一引用其程序集的是 `src/MinGo.ReverseProxy/MinGo.ReverseProxy.csproj` |
| `src/MinGo.ReverseProxy` | 不在 `.slnx` 的激活项目列表（已被注释）；无任何活项目 `ProjectReference` 它；功能由 `MinGo.ControlPlane.Api` + `frontend/min-go-console` 承接 |
| 根 `Dockerfile` | 第 19/25/43/47/59/88 行只围绕 `src/MinGo.ReverseProxy` 构建；`.github/workflows/build.yml:21,25` 与 `.gitea/workflows/build.yml:20,24` 都显式使用 `ControlPlane.Dockerfile` / `DataPlane.Dockerfile`；compose 两个文件同样只用拆分 Dockerfile |
| `CertificateManager` / `ICertificateManager` / `IServerCertificateSelector` | 唯一注册点是 `src/MinGo.ReverseProxy/Kestrel/Extensions.cs:16-18`；控制面 `Program.cs:30-126` 的注册列表里没有它们；`InitializeCertificatesAsync`（`MinGo.Infrastructure/Extensions.cs:41`）全仓零调用者 |

### 3.2 删除，而不是移进 `archived/`

仓库确实有归档习惯：`archived/MinGo.ControlPlane`、`archived/MinGo.Gateway` 共 57 个被跟踪文件。所以"移进 archived/"是一个真实存在且更保守的选项，本 change 明确拒绝它：

- 本轮探索已经发生过一次真实误读——读 `ProxyConfigEntities.cs` 时命中 `MinGo.Shared` 那份，导致对活模型的判断出错。归档不解决这个根因，因为 `archived/` 同样会被 grep / IDE 索引 / AI 检索命中（本轮 grep 结果里 `archived/MinGo.ControlPlane/wwwroot/js/theme.js` 就是这样混进来的）
- 死副本已经在语义上过期：它保留 `ClusterConfig.Name` / `ApiClusterEntity.Name`，而 `cluster-name-is-id` 规格写明 `Name` SHALL NOT 单独存在。留着它等于在仓库里存了一份"与现行规格矛盾的样板"
- 需要参考旧实现时，git 历史 + `2026-06-03-split-data-control-plane` 归档 change 已经足够

代价：删除后无法在磁盘上直接对照旧单体实现。可接受。

### 3.3 根 `Dockerfile` 直接删，不改写成"聚合入口"

替代方案是把根 `Dockerfile` 重写为同时产出两个镜像的多阶段聚合文件。拒绝理由：两个 CI 已经各自指定 Dockerfile，聚合入口会成为第三个需要保持一致的构建定义，正是本次要消除的那类漂移源。本地一键构建由 `docker compose build` 覆盖（compose 里已带 `build.dockerfile`）。

### 3.4 `.slnx` 删注释行而不是留着

`<!-- TODO: 迁移完成后移除以下项目 -->` 加两行注释 `<Project>` 在迁移完成后继续存在，会让读的人以为迁移尚未收尾。TODO 已履行，连注释一起删。

### 3.5 不加 `.gitignore` 规则

`src/MinGo.ReverseProxy/` 下的 `gateway.db`、`logs/`、`node_modules/` 均已被现有 `.gitignore` 覆盖（`*.db` 第 75 行、`logs/` 第 85 行、`node_modules/` 第 54 行），删除目录后无需任何 ignore 变更。

### 3.6 证书孤儿与单体同批删除

`CertificateManager` 的物理删除动作发生在 `MinGo.Infrastructure` / `MinGo.Core`，不在"删掉的两个目录"里，看起来像越界。放进本 change 的原因是因果而非位置：它变成不可达**正是**删除 `src/MinGo.ReverseProxy` 的直接后果（那三行 `AddSingleton` 是它唯一的注册点）。分开做会留下一个中间状态——仓库里存在一个实现了 `IServerCertificateSelector` 的死类，而活的数据面选择器 `DataPlaneCertificateSelector` 名字和职责都与它重叠，正是本 change 要消除的那类"同名异味"。

与 `unify-certificate-validity` 的分工：本 change 只删。`CertificateManager.cs:154-163` 里唯一有价值的东西——同域名多张证书按到期时间择优——由那个 change 在控制面下发侧重建（它自己的 design 3.5），不在这里保留死类。

### 3.7 `ReverseProxyExtensions` 整类删除，`DatabaseProxyConfigProvider` 对偶留给后续

`src/MinGo.Infrastructure/Extensions.cs:20-125` 的 `ReverseProxyExtensions` 有 5 个方法，逐个核实调用者为 0：`LoadFromDatabase`、`InitializeCertificatesAsync`、`UseDevelopmentAutoMigration`、`InitializeDatabaseProxyConfigAsync`、`ConfigureKestrelSni`。整类删除比逐个挑更省事，也不会留下"半个死扩展类"。同文件的 `HttpClientExtensions.AddNamedHttpClients` 同样零调用者，但它与证书/单体无关，保留。

删掉这个类以后，`ExternalServices/DatabaseProxyConfigProvider.cs` 与 `ExternalServices/ConfigUpdateEventListener.cs` 只剩彼此这一个引用（后者实现 `IHostedService`，但全仓没有 `AddHostedService<ConfigUpdateEventListener>` 注册）。这组自洽死对留给后续单独处理：事件订阅语义与控制面 `ConfigUpdateGrpcBroadcaster` 那条活路径名字相近，需要一次独立确认，不值得把本 change 的"零行为变更"承诺拖成需要逐条论证的边界。

### 3.8 活选择器与死选择器的命名冲突

删除后，`IServerCertificateSelector` 这个类型消失，`MinGo.DataPlane.Kestrel.DataPlaneCertificateSelector` 成为仓库里唯一的证书选择实现。这是本 change 里对可读性收益最大的一刀：`unify-certificate-validity` 要改的就是这个活类，而在此之前任何人 grep "CertificateSelector" / "CertificateManager" 都会撞上两个候选。

## 4. 事实依据

- `MinGo.ReverseProxy.slnx` 当前激活项目 5 个 + 测试 2 个，另有两个注释项与 TODO
- 被跟踪文件：`src/MinGo.Shared` 7 个、`src/MinGo.ReverseProxy` 28 个（6 个 Controller、`Kestrel/` 2 个、`Services/AuthService.cs`、csproj、launchSettings、appsettings ×2、vite/ts 配置 4 个、`index.css`、`package.json`、`pnpm-lock.yaml`、`wwwroot/css/open-iconic/**` 字体资产）
- 活模型 `MinGo.Core.Models.ClusterConfig` 字段为 `Id` / `Destinations` / `LoadBalancingPolicy` / `HealthCheck`，无 `Name`；`MapToClusterModel`（`ApiDbService.cs:593-608`）同样不映射 `Name`
- `docs/GITEA_ACTIONS_WORKFLOW.md:112` 要求"项目根目录需要存在有效的 `Dockerfile`"，与两个 CI 的实际配置矛盾

## 5. Risks / Trade-offs

### 5.1 外部脚本可能直接 `-f Dockerfile`

仓库外若有手工命令或旧部署脚本引用根 `Dockerfile`，删除后立刻失败。缓解：本 change 的验收任务包含全仓 grep 复核 + 一次 `dotnet build` 与两个拆分镜像的构建；如确认存在外部依赖，改为保留一个只转发到拆分 Dockerfile 的说明性文件（而非继续构建单体）。

### 5.2 文档更新可能被跳过

`GITEA_ACTIONS_WORKFLOW.md` 那句要求不删，下一个读者会重新造一个构建单体的 Dockerfile。列为独立任务而非附带。

### 5.3 删除后无法本地对照旧实现

见 3.2 的代价说明，靠 git 历史回看。

### 5.4 证书孤儿清单可能在实施时过期

3.6/3.7 的"零调用者"结论是本轮探索的快照。`ReverseProxyExtensions` 的方法是扩展方法，任何新项目一行 `app.UseDevelopmentAutoMigration()` 就能让它复活。缓解：删除前逐条复核（tasks 1.4）、删除后 grep 复核（tasks 4.4），两侧都留了检查而不是只靠 proposal 里的结论。

### 5.5 与 `unify-certificate-validity` 的顺序

`CertificateManager.cs:154-163` 的"同域名多张证书按到期时间择优"是本 change 删除范围内唯一有价值的语义，那个 change 已把它重建到控制面下发侧（其 design 3.5）。两个 change 无强顺序依赖，但**本 change 先落地更好**：先删掉死类，避免实施证书有效性统一时有人图省事直接改 `CertificateManager` 而不是改活的下发链路。

## 6. Migration Plan

### 6.1 执行顺序

1. 全仓 grep `src/MinGo.ReverseProxy`、`MinGo.Shared` 复核引用面（活代码只应剩 csproj 互引与文档）
2. `git rm -r src/MinGo.Shared src/MinGo.ReverseProxy` + `git rm Dockerfile`
3. 改 `.slnx`（删两行注释与 TODO）
4. `dotnet build MinGo.ReverseProxy.slnx -m:1` 必须成功
5. 分别构建 `ControlPlane.Dockerfile` 与 `DataPlane.Dockerfile`
6. 改 `docs/GITEA_ACTIONS_WORKFLOW.md:112`

顺序的意义在于第 1、4 步是删除前后的两次独立验证，任何一步失败都可以整步回退——本 change 不需要分阶段发布，一次提交完成。
