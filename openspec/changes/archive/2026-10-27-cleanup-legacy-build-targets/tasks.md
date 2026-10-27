## 1. 删除前复核

- [x] 1.1 全仓 grep `src/MinGo.ReverseProxy` 与 `MinGo.Shared` 的引用面，确认除旧单体自身 csproj、`archived/`、`openspec/changes/archive/`、`docs/` 之外无活代码引用
- [x] 1.2 确认仓库外没有直接 `-f Dockerfile` 的脚本或部署流程（design 5.1）；若存在，改为保留一个只指向拆分 Dockerfile 的说明性文件，而不是继续构建单体
- [x] 1.3 `git ls-files src/MinGo.Shared src/MinGo.ReverseProxy` 记录基线（7 + 28 个被跟踪文件），删除后核对无多余丢失
- [x] 1.4 删除前逐条复核证书孤儿的引用面（本轮探索已核实一次，实施时重新执行以免期间新增引用）：`CertificateManager` / `ICertificateManager` / `IServerCertificateSelector` 的注册点只剩 `MinGo.ReverseProxy/Kestrel/Extensions.cs:16-18`；`X509CertificateHelpers` 零引用；`ReverseProxyExtensions` 的 5 个方法（`LoadFromDatabase` / `InitializeCertificatesAsync` / `UseDevelopmentAutoMigration` / `InitializeDatabaseProxyConfigAsync` / `ConfigureKestrelSni`）调用者均为 0。任一条件不成立就停下修正清单

## 2. 删除

- [x] 2.1 `git rm -r src/MinGo.Shared`（`MinGo.Shared.Models` 全仓 0 引用，活模型是 `MinGo.Core.Models`）
- [x] 2.2 `git rm -r src/MinGo.ReverseProxy`（拆分前单体：6 个旧 Controller、`Kestrel/`、`Services/AuthService.cs`、旧 vite 配置与 `wwwroot` 字体资产）
- [x] 2.3 `git rm Dockerfile`（88 行，第 19/25/43/47/59/88 行只服务于已退役单体）
- [x] 2.4 删除证书孤儿：`src/MinGo.Infrastructure/Services/CertificateManager.cs`、`src/MinGo.Core/Interfaces/ICertificateManager.cs`（含 `CertificateInfo`）、`src/MinGo.Infrastructure/Kestrel/IServerCertificateSelector.cs`、`src/MinGo.Infrastructure/Kestrel/X509CertificateHelpers.cs`（唯一注册者在 2.2 删除的 `MinGo.ReverseProxy/Kestrel/Extensions.cs:16-18`；`X509CertificateHelpers` 已核实全仓零引用，`Kestrel/` 目录随之为空）
- [x] 2.5 `src/MinGo.Infrastructure/Extensions.cs`：删除 `ReverseProxyExtensions` 整类（原第 17-122 行，5 个方法逐个核实调用者为 0），保留 `HttpClientExtensions`
- [x] 2.6 本地清理 `src/MinGo.ControlPlane`、`src/MinGo.Gateway` 空壳目录（被跟踪文件数为 0，属本地 `bin/obj/node_modules` 残留，不进提交）

## 3. 解决方案与文档

- [x] 3.1 `MinGo.ReverseProxy.slnx`：删除两行注释掉的 `<Project Path>`（`MinGo.ReverseProxy`、`MinGo.Shared`）与 `<!-- TODO: 迁移完成后移除以下项目 -->`
- [x] 3.2 `docs/GITEA_ACTIONS_WORKFLOW.md:112`：把"项目根目录需要存在有效的 `Dockerfile`"改为描述两个拆分 Dockerfile 的实际用法
- [x] 3.3 检查 `docs/` 与 README 中对根 `Dockerfile` 的其余引用（design 5.2）：仅 `docs/GITEA_ACTIONS_WORKFLOW.md` 有"必须存在根 Dockerfile"的规范性表述（已改）；`docs/技术设计方案.md` 与 `docs/API网关产品文档.md` 里的旧单体目录树/旧 `COPY` 代码块属于拆分前的历史设计文档，不在本 change 逐行重写（见交付说明）

## 4. 验证

- [x] 4.1 `dotnet build MinGo.ReverseProxy.slnx -m:1` 成功（0 错误；顺带修复 `32fa03c` 重构 `DataPlaneConfigProvider` 构造函数后遗留的 `DataPlaneConfigProviderTests` 签名不匹配 —— 该测试项目在主分支上原本无法编译）
- [x] 4.2 `dotnet test`（`tests/MinGo.Core.Tests`、`tests/MinGo.DataPlane.Tests`）通过：59 + 43 = 102 个测试全绿
- [x] 4.3 分别 `podman build -f ControlPlane.Dockerfile` 与 `-f DataPlane.Dockerfile` 成功（需清掉容器内继承的 `HTTP_PROXY=127.0.0.1:7890`，见交付说明）
- [x] 4.4 删除后 grep 复核：`src/` 内 `CertificateManager`、`ICertificateManager`、`IServerCertificateSelector`、`X509CertificateHelpers`、`ReverseProxyExtensions`、`MinGo.Shared` 全部零命中；`DatabaseProxyConfigProvider` 与 `ConfigUpdateEventListener` 只剩彼此一个引用（design 3.7 的后续边界成立）
- [x] 4.5 `openspec validate cleanup-legacy-build-targets --strict` 通过
