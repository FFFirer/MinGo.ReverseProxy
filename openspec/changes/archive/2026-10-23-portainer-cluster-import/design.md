# Design: Portainer 导入生成 Cluster

## Context

两套实例的 spike 结论（`docs/spikes/portainer/README.md`）：

| | debian13（2.45.0） | debian12（2.39.3） |
|---|---|---|
| Endpoint | 2 个 Podman daemon（rootful + rootless） | 1 个 Docker daemon |
| 容器 / compose 项目 | 14 / 10 | 49 / 32（另有 6 个无 label 容器） |
| `/api/stacks` | 8（缺 portainer-agent） | **12（实际 33 项目）** |
| 网络拓扑 | 业务容器全在 `default_bridge`，与 data-plane 同网 | 每项目独立 bridge，data-plane 单独一网 |
| 容器名可用 | ✓（EP4） | ✗（绝大多数） |

由此确定三条硬约束：**容器优先枚举**、**两种地址形态**、**永不用容器 IP**。

## Decisions

### D1. 只读发现，写路径零新增

前端拿到发现结果后，新建 cluster 调既有 `POST /api/apimanagement/clusters`；追加到已有 cluster 调 `GET` 取现状 → 合并（保留已有 id、新行不带 id）→ `PUT`。

理由：`auto-destination-id` 已规定 id 由后端生成、`destination-array-format` 已规定数组语义，导入只需产出"预填好的表单"。避免两套写入校验，也避免绕过将来 `config-validation-before-push` 的 `ConfigValidator`。

否决方案：新增 `POST /portainer/import` 直接落库。

### D2. 容器优先，不使用 `/api/stacks`

用于发现的只有三个只读端点：

```
GET /api/endpoints                                → endpoint 列表
GET /api/endpoints/{id}/docker/containers/json?all=1
GET /api/endpoints/{id}/docker/networks           → 仅取网络 driver/默认标记
```

`/api/stacks` **不参与发现**：2.39 只返回 Portainer 自建 stack（12/33），会漏掉 gitea、minio、redis 等外部 compose 项目。分组一律用容器的 `com.docker.compose.project` label；无 label 的容器归入 `(未分组)` 桶（`source: "standalone"`）。

网络归属从容器侧 `NetworkSettings.Networks` 推导——2.39 的 `/networks` 返回 `Containers` 为空映射。

### D3. 两种地址形态，永不使用容器 IP

| 形态 | 生成规则 | 可用条件 |
|---|---|---|
| 容器名 | `http://{containerName}:{privatePort}` | 网络为 user-defined（driver=bridge 且 name≠`bridge`）**且** `dataPlaneAttached=true` |
| 宿主 | `http://{host}:{publishedPort}` | 存在发布端口；`host` 取绑定 IP（非 `0.0.0.0`/`::`），否则取配置 `HostAddress` |

`dataPlaneAttached`：在目标 (endpoint, network) 中查找满足 `com.docker.compose.service == DataPlaneService` 且 `com.docker.compose.project ∈ SelfStackNames` 的运行中容器。

- debian13 EP4 / `default_bridge` → true（`reverseproxy-data-plane-1`）
- debian13 EP2、debian12 全部网络 → false

容器 IP 一律不提供：两个 Podman daemon 的 10.89.x 子网重叠（EP2 `adguardhome` 与 EP4 `sid-idserver` 同为 `10.89.1.4`），且容器重建即失效。

### D4. 自我识别：主机名 → 容器 Id → compose project（已验证）

三个部署的 inspect 证据见 `docs/spikes/portainer/README.md`：容器内 `Environment.MachineName` 恒等于自身 Container Id 前 12 位。据此推导自身 stack，不硬编码名字。

解析链（短路）：

```
config SelfStackNames 非空 ──▶ 采用（人工覆盖，最高优先）
        │
        ▼
Environment.MachineName（= 容器内 HOSTNAME）──▶ containers[].Id.StartsWith(hostname)
        │                                        │
        │                                   命中 ──▶ com.docker.compose.project = self stack
        ▼
未命中 ──▶ SelfImagePatterns（默认 ["reverseproxy-cp"]）匹配镜像 ──▶ project
        │
        ▼ （仍失败）
运行在容器内（DOTNET_RUNNING_IN_CONTAINER / /.dockerenv）──▶ self.detected=false，UI 告警，不阻断
```

- 同一 project 内 `service == DataPlaneService` 的容器即自身 data-plane（用于 D3 的 `dataPlaneAttached`）
- 系统容器不靠名字：按 label `io.portainer.server=true` / `io.portainer.agent=true` 排除
- **跨实例不误伤**：CP 在 debian12、Portainer 指向 debian13 时，主机名匹配不上 ⇒ self 为空，debian13 上的另一个 `reverseproxy` 栈照常可选。这正是不能把 `reverseproxy` 写死进默认列表的原因（debian12 上还有 `mingo-reverseproxy` 与 `1` 两个并存部署）
- 排除原因枚举：`self-stack` / `portainer-system` / `not-running` / `extra-excluded`

响应带 `self` 与 `excluded[]`；前端显示"已识别自身 stack: X（自动）"，未识别时显示告警并提示可用 `SelfStackNames` 覆盖。

### D5. 人工勾选，默认不预选

```
1 Endpoint ──▶ 2 网络 ──▶ 3 容器 × 端口 ──▶ 4 目标与确认
  唯一则自动选    按网络分组    复选框 + 形态开关      新建 / 追加
                              实时预览最终地址         slug 预填、可改
```

- 端口：列出 private + published；多端口容器（如 aspire 4317/18888、adguardhome 3000/80/53）逐项勾选
- 形态：不满足条件时禁用并说明原因（"默认 bridge 不支持容器名解析" / "data-plane 未接入此网络"）
- host 值：默认为配置值，预览中可逐条编辑（应对 LAN / Tailscale 不同入口）
- 新建 cluster：一个勾选集合 = 一个 cluster，名称取 stack 名 slug；追加：按地址去重后并入目标 cluster

### D6. 命名与冲突

stack 名 slug 化：小写、空白转 `-`、仅保留 `[a-z0-9._-]`、折叠连续 `-`、去首尾；为空回退 `stack-{endpointId}`。
地址去重遵循"地址即标识符"。`POST` 返回 409 时提示重名并聚焦名称框，不自动改名。

### D7. 配置驱动、只读 UI

```json
"Portainer": {
  "Enabled": false,
  "BaseUrl": "http://100.94.195.53:9000",
  "ApiKey": "",
  "TimeoutSeconds": 10,
  "CacheSeconds": 30,
  "HostAddress": "192.168.88.134",
  "DataPlaneService": "data-plane",
  "SelfStackNames": [],
  "SelfImagePatterns": ["reverseproxy-cp"],
  "ExtraExcludedStacks": []
}
```

`SelfStackNames` 留空即自动检测（D4）；填充则视为人工覆盖。`ExtraExcludedStacks` 用于额外隐藏（例如 debian12 上遗留的 `1` 实例）。

环境变量覆盖：`Portainer__BaseUrl`、`Portainer__ApiKey`。Settings 页只读展示 + "测试连接"；`ApiKey` 不出现在任何响应中，仅回 `apiKeyConfigured: true/false`。

### D8. 发现接口一次返回、短期缓存

`GET /api/portainer/discovery`（`endpointId` 可选过滤）

```jsonc
{
  "connected": true,
  "version": "2.45.0",
  "self": {
    "detected": true,
    "source": "hostname",          // hostname | config | image | none
    "stacks": ["reverseproxy"],
    "containerCount": 3
  },
  "endpoints": [{
    "id": 4, "name": "rootless", "engine": "podman", "status": "up",
    "networks": [{
      "name": "default_bridge", "driver": "bridge",
      "isDefaultBridge": false,
      "dataPlaneAttached": true,
      "groups": [{
        "name": "staticpages", "source": "compose",
        "containers": [{
          "id": "…", "name": "static-pages", "service": "static-pages",
          "ports": [{
            "private": 8080, "public": 5218, "bindIp": "0.0.0.0", "protocol": "tcp",
            "addresses": {
              "containerName": "http://static-pages:8080",
              "host": "http://192.168.88.134:5218"
            }
          }]
        }]
      }]
    }]
  }],
  "excluded": [{ "name": "reverseproxy-control-plane-1", "reason": "self-stack" }]
}
```

缓存 30 秒（`IMemoryCache`）；`GET /api/portainer/status` 不缓存，返回 `baseUrl`、`selfDetection` 与连通性供 Settings 卡片和向导入口使用（`ApiKey` 仅返回 `apiKeyConfigured`）。

### D9. 失败降级

- 未配置 / 不可达 / token 失效：`discovery` 返回 503 + 可读原因，Clusters 手填流程不受影响，导入按钮置灰
- 单 endpoint 失败：标记 `error`，其他 endpoint 正常返回
- 超时 10 秒，不自动重试

## Risks

- **容器名依赖 data-plane 入网**：`dataPlaneAttached` 只反映当下；网络调整后地址失效。向导需明示该前提。
- **自动识别依赖容器主机名**：compose 显式设置 `hostname:` 或使用 `network_mode: host` 时，主机名不再等于容器 Id；此时回退 `SelfImagePatterns`，仍不命中则告警，可用 `SelfStackNames` 覆盖。
- **端口绑定在具体 IP**（如 adguardhome 53@192.168.88.134）：host 形态必须沿用它，不能替换为配置值。
- **容器无 aliases 字段**（debian12）：不能据此判断名称可解析，只能按网络类型推导。
- **Token 权限**：账号级凭据，放环境变量，不入库、不回显、不写回 Portainer。
- **两实例版本差异**：只依赖 2.39 与 2.45 均验证过的端点，不引入版本分支。

## Testing

- 单元测试 `tests/MinGo.Core.Tests`（纯函数：地址推导、自我排除、自身识别、data-plane 判定、slug）
  - 矩阵：user-defined / 默认 bridge / host 模式 / 无发布端口 / 具体绑定 IP / 无 aliases
  - `dataPlaneAttached`：debian13 EP4 = true；EP2 与 debian12 = false
  - 排除：self stack、portainer-agent、exited（`sid-migrate-1`）、oneoff
- Fixture：两实例真实 JSON（`docs/spikes/portainer/**`，无 token）
- 手工：对两台 Portainer 各打开一次向导；EP4 生成容器名地址、debian12 仅 host 地址；导入后 `GET /clusters/{id}` 与手填结构一致
- 回归：`dotnet build`、`dotnet test`、`cd frontend/min-go-console && pnpm build`

## Migration

无数据库变更；`Portainer:Enabled` 默认 `false`。
