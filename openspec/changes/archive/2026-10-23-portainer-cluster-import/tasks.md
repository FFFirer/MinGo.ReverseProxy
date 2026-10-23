## 1. 配置与 Portainer 客户端

- [x] 1.1 新增 `src/MinGo.ControlPlane.Api/Configuration/PortainerOptions.cs`（Enabled、BaseUrl、ApiKey、TimeoutSeconds、CacheSeconds、HostAddress、DataPlaneService、SelfStackNames、SelfImagePatterns、ExtraExcludedStacks）
- [x] 1.2 `appsettings.json` 增加 `Portainer` 节（默认 `Enabled=false`、空凭据）；`docker-compose.yml` / `docker-compose.local.yml` 预留 `Portainer__BaseUrl`、`Portainer__ApiKey` 环境变量
- [x] 1.3 新增 `src/MinGo.ControlPlane.Api/Services/PortainerClient.cs`：typed HttpClient，BaseAddress + `X-API-KEY`，方法 `GetStatusAsync` / `GetEndpointsAsync` / `GetContainersAsync(endpointId)` / `GetNetworksAsync(endpointId)`；10s 超时，无重试
- [x] 1.4 新增 Portainer 原始响应 DTO（`Models/Portainer/PortainerApiDtos.cs`）；因公共客户端方法签名需要，类型为 public，但不含任何凭据
- [x] 1.5 `Program.cs` 注册 `Configure<PortainerOptions>`、`AddMemoryCache`、`AddHttpClient<PortainerClient>` 与发现服务；未配置时客户端可构造但不发请求

## 2. 领域纯函数（MinGo.Core/Portainer）

- [x] 2.1 新增 `DiscoveryModels.cs`：`ContainerSnapshot` / `NetworkContext` / `AddressCandidates` / `ExcludedContainer` / `SelfIdentity` 等
- [x] 2.2 新增 `DestinationAddressResolver.cs`：容器名形态（user-defined 且 dataPlaneAttached）、宿主形态（具体绑定 IP 优先，否则 HostAddress）、不含容器 IP
- [x] 2.3 新增 `SelfIdentityResolver.cs`：按优先级解析自身 stack —— 配置 `SelfStackNames` → `Environment.MachineName` 匹配 `container.Id` 前缀 → `SelfImagePatterns` 兜底；输出 `source`(config/hostname/image/none) 与命中 project 集合
- [x] 2.4 新增 `SelfExclusionPolicy.cs`：排除非 running、self stack 容器、`io.portainer.server=true` / `io.portainer.agent=true`、`ExtraExcludedStacks`；输出原因 `not-running` / `self-stack` / `portainer-system` / `extra-excluded`
- [x] 2.5 新增 `DataPlaneDetector.cs`：在 self project 内按 `service == DataPlaneService` 判定目标网络是否存在 data-plane
- [x] 2.6 新增 `ClusterNameSlugger.cs`：小写、空白转 `-`、保留 `[a-z0-9._-]`、折叠 `-`、去首尾、空值回退
- [x] 2.7 按地址去重、保留已有 destination id：在前端 `PortainerImportModal.handleSubmit` 的 GET → 合并 → PUT 流程中实现，未新增后端 `ClusterDestinationMerger`

## 3. 发现服务与 API

- [x] 3.1 新增 `src/MinGo.ControlPlane.Api/Services/PortainerDiscoveryService.cs`：按 endpoint 并行取容器/网络 → 解析自身身份（`SelfIdentityResolver`）→ 绑定网络归属 → 分组（compose/standalone）→ 地址推导 → 排除；单 endpoint 失败标记 `error`
- [x] 3.2 接入 `IMemoryCache`，按 `CacheSeconds` 缓存完整发现结果；`status` 绕过缓存并顺带刷新缓存
- [x] 3.3 新增 `src/MinGo.ControlPlane.Api/Models/Portainer/*.cs` 响应 DTO（self / endpoints / networks / groups / containers / addresses / excluded）
- [x] 3.4 新增 `src/MinGo.ControlPlane.Api/Controllers/PortainerController.cs`，`[Authorize]`：
  - `GET /api/portainer/status`（不缓存，不返回凭据，含 `baseUrl` / `selfDetection`）
  - `GET /api/portainer/discovery?endpointId=`（未配置 → 503）
- [x] 3.5 错误处理：Portainer 401/403/超时/连接失败转换为 503 ProblemDetails；endpoint 不存在的 4xx 原样返回；记录 Warning 日志，不抛 500

## 4. 测试

- [x] 4.1 新建 `tests/MinGo.Core.Tests/MinGo.Core.Tests.csproj`（xunit + coverlet，CPM 引用），加入 `MinGo.ReverseProxy.slnx`
- [x] 4.2 以代码内快照 `tests/MinGo.Core.Tests/Portainer/Snap.cs` 复刻两实例的真实容器名/stack/端口（未落盘原始 JSON）
- [x] 4.3 地址推导矩阵测试：user-defined、默认 bridge、host 模式、无发布端口、具体绑定 IP（adguardhome 53）、无 aliases（debian12）
- [x] 4.4 dataPlane 判定测试：debian13 EP4 `default_bridge` = true；EP2 = false；debian12 全网络 = false
- [x] 4.5 自我识别测试：用真实 Id 前缀模拟主机名（`d69dc8afd9d4` → `reverseproxy`；`11c3258b03d3` → `mingo-reverseproxy`；`3f8782a215bf` → `1`）；未命中时不排除任何栈；配置覆盖优先于自动检测
- [x] 4.6 排除测试：self stack、`io.portainer.agent`、`io.portainer.server`、exited（`sid-migrate-1`）、`ExtraExcludedStacks`
- [x] 4.7 slug 测试完成；合并去重逻辑在前端，暂无自动化测试
- [ ] 4.8 客户端解析测试（fixture 反序列化端点/容器/网络）未补；当前仅覆盖纯函数，真实实例由手工验证兜底

## 5. 前端基础

- [x] 5.1 `src/types/index.ts` 增加 `PortainerStatus` / `PortainerDiscovery` 及子类型
- [x] 5.2 `src/api/client.ts` 增加 `getPortainerStatus()` / `getPortainerDiscovery()`；错误解析兼容 `application/problem+json`

## 6. 前端导入向导

- [x] 6.1 新增 `src/components/shared/PortainerImportModal.tsx`：四步（endpoint → 网络 → 容器×端口 → 目标与确认），支持上一步/关闭重置
- [x] 6.2 网络步骤：显示 driver 与 `dataPlaneAttached` 徽标；默认 bridge 或未接入时禁用容器名形态并给出原因
- [x] 6.3 容器步骤：按分组展示、复选框、端口行、实时展示两种候选地址与最终地址（可编辑）
- [x] 6.4 目标步骤：新建 cluster（slug 预填、可改、409 提示）或追加到已有 cluster（下拉 + 合并预览 + 地址去重）
- [x] 6.5 提交：新建 `POST /api/apimanagement/clusters`；追加 `GET` → 合并 → `PUT`；成功后刷新列表并 toast
- [x] 6.6 展示自我识别结果：`self.detected=true` 显示"已识别自身 stack X（来源：…）"；`false` 显示告警条并提示配置 `Portainer:SelfStackNames`

## 7. 前端入口与只读状态

- [x] 7.1 `src/pages/Clusters.tsx` 增加"从 Portainer 导入"按钮：未配置/不可达/未配置 ApiKey 时置灰 + tooltip；接入向导
- [x] 7.2 `src/pages/Settings.tsx` 增加只读 Portainer 卡片：BaseUrl、enabled、版本、endpoint 数、apiKeyConfigured、自我识别结果（来源 + stack 列表）、"测试连接"按钮
- [x] 7.3 追加模式下从当前 cluster 详情进入时预选目标 cluster

## 8. 验证

- [ ] 8.1 `dotnet build MinGo.ReverseProxy.slnx`：`MinGo.Core` / `MinGo.ControlPlane.Api` 构建通过；slnx 整体仍被既有 `MinGo.DataPlane.Tests` 编译错误阻塞（`InMemoryConfigProvider` → `IServiceProvider`，非本变更引入）
- [x] 8.2 `dotnet test tests/MinGo.Core.Tests`：59 通过 / 0 失败
- [x] 8.3 `cd frontend/min-go-console && pnpm build`：成功（49 modules）
- [x] 8.4 手工 debian13（2.45.0）：EP4 `default_bridge`（data-plane 接入）出现容器名形态；EP2 只有宿主形态；切换"容器名/宿主"地址即时变化
- [x] 8.5 手工 debian12（2.39.3）：发现 10 个网络、24 个 compose/standalone 分组（不依赖 `/api/stacks`），全部只有宿主形态；未发布端口的行正确禁用
- [x] 8.6 手工：通过向导新建 cluster（host 形态）与追加 destination（GET→合并→PUT）均成功，结构符合 `GET /api/apimanagement/clusters/{id}`；容器名形态 cluster 也通过 UI 创建成功；测试数据已删除。未用 route 实测 data-plane 转发
- [x] 8.7 手工：debian12 上 `mingo-reverseproxy` 自身栈被排除，`1` 实例可选；ExtraExcludedStacks 由单元测试覆盖
- [ ] 8.8 已知行为差异：当 CP 不在目标 Portainer 内时，镜像兜底 (`SelfImagePatterns`) 仍会把远端 `reverseproxy-cp` stack 识别为自身并排除。这是为"容器内自定义 hostname"场景保留的兜底策略；如需"只信任主机名识别"，可后续增加配置开关
