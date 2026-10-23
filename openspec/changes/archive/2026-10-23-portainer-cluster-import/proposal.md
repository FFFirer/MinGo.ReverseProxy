## Why

Cluster 与 destination 目前只能手填地址。宿主机上已有 8 个 compose 应用由 Portainer 管理，每个应用的容器名、网络、端口都是已知事实，手填既慢又容易写错（尤其发布端口是 5218/9076/9873 这类随机数）。

目标是让控制台从 Portainer **只读发现**这些应用，人工勾选后直接生成或追加 cluster。范围严格限定为"简化 cluster 的增改"：不生成 Route、不持续同步、不回写 Portainer。

证据见 `docs/spikes/portainer/README.md`（Portainer 2.45.0 CE，两个 Podman endpoint）。

## What Changes

- **新增 Portainer 只读客户端**：`X-API-KEY` 认证，配置驱动（`Portainer:BaseUrl` / `Portainer:ApiKey`，支持环境变量覆盖），不做 DB CRUD
- **新增发现接口**：`GET /api/portainer/status`（连通性 + 版本）、`GET /api/portainer/discovery`（endpoint → 网络 → stack → 运行中容器 → 端口 → 候选地址），30 秒内存缓存
- **地址推导规则**：容器名形态（仅 user-defined 网络且 data-plane 在该网络时可用）与 host:发布端口形态；**不提供容器 IP 形态**（两个 daemon 子网重叠，IP 有歧义）
- **自我识别与排除**：不硬编码 stack 名——容器内 `Environment.MachineName` 匹配容器 Id 前缀，推出自身 compose project；Portainer 系统容器按 `io.portainer.*` 标签排除；非 running 容器不出现；识别失败时前端告警，可用 `SelfStackNames` 覆盖
- **前端导入向导**：Clusters 页新增"从 Portainer 导入"，选 endpoint → 选网络 → 勾选 容器×端口 → 选目标（新建 cluster / 追加到已有 cluster）→ 预览确认
- **写入复用既有 API**：新建走 `POST /api/apimanagement/clusters`，追加走 `GET` + 合并 + `PUT`，不新增写端点、不改 destination 自动 ID 语义
- **Settings 页只读状态卡**：展示启用状态、地址、版本、endpoint 数、测试连接按钮；不可编辑、不回显 token

## Capabilities

### New Capabilities

- `portainer-cluster-import`: 从 Portainer 只读发现容器并生成/追加 cluster 的完整能力，含配置、发现、地址推导、自我排除、人工确认与写入复用规则

### Modified Capabilities

无。导入是 `auto-destination-id` / `destination-array-format` 的第二个输入端，语义不变。

## Impact

- **MinGo.ControlPlane.Api**：新增 `PortainerOptions`、`PortainerClient`、`PortainerDiscoveryService`、`PortainerController`；`Program.cs` 注册 typed HttpClient 与 options
- **MinGo.Core**：新增 `Portainer/` 下纯函数（地址推导、stack 名 slug、自我排除过滤），便于单测
- **前端**：`types/index.ts`、`api/client.ts`、`pages/Clusters.tsx`、新增 `components/shared/PortainerImportModal.tsx`、`pages/Settings.tsx`
- **测试**：新增 `tests/MinGo.Core.Tests`，以本次 spike 的真实容器/网络/端口快照构造用例（`Portainer/Snap.cs`）
- **配置**：`appsettings.json` 增加 `Portainer` 节；`docker-compose.yml` 预留 `Portainer__BaseUrl` / `Portainer__ApiKey` 环境变量（默认关闭）
- **无数据库迁移**，不改 DataPlane / gRPC 复制路径
