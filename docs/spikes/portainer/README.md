# Portainer Spike 证据（2026-10-23）

只读采集，未写入任何 Portainer 资源。API token 未落盘。`stacks.json` 中 stack 环境变量的凭据与密钥值已替换为 `<redacted>`。

## 实例

| 项 | 值 |
|---|---|
| Base URL | `http://100.94.195.53:9000`（debian13，Tailscale） |
| 版本 | Portainer **2.45.0**（`/api/licenses` → 404 ⇒ CE） |
| 认证 | `X-API-KEY`，管理端点全部 401 无 token |
| 另有一台 | `http://100.102.18.111:9000` = 2.39.3（debian12，未被本次使用） |

## Endpoints（`/api/endpoints`）

| Id | Name | Type | Engine | URL | Swarm |
|---|---|---|---|---|---|
| 2 | local | 1（本地 socket） | podman 5.4.2 | `unix:///var/run/docker.sock` | inactive |
| 4 | rootless | 2（TCP agent） | podman 5.4.2 | `tcp://192.168.88.134:9001` | inactive |

两个 endpoint 是**同一台 debian13 上的两个独立 Podman daemon**（rootful / rootless）。

## 网络与关键容器

### Endpoint 4（rootless）— 业务网 `default_bridge` 10.89.1.0/24

9 个运行中容器挂在同一张 user-defined bridge 上，因此容器名可互解析：

| 容器 | compose project | 内部端口 | 发布端口 |
|---|---|---|---|
| reverseproxy-data-plane-1 | reverseproxy | 8080 / 8443 | 9080 / 9081 |
| reverseproxy-control-plane-1 | reverseproxy | 5000 | 5000 |
| static-pages | staticpages | 8080 | 5218 |
| app-portal | app-portal | 8080 | 8080 |
| sub-store | sub-store | 3001 | 3001 |
| aiworkbench | aiworkbench | 8080 | 9076 |
| sid-idserver-1 | sid | 5001 | 9873 |
| sid-admin-1 | sid | 5002 | 9874 |
| aspire-dashboard-… | aspire-dashboard | 18888 | 18888 |
| portainer-agent | portainer-agent | 9001 | 9001 |

另有 `sid-migrate-1`、`reverseproxy-migrate-1` 为 `exited` + `oneoff`，必须过滤。

### Endpoint 2（rootful）— `adguardhome_dns-net`

| 容器 | compose project | 内部端口 | 发布端口 |
|---|---|---|---|
| adguardhome | adguardhome | 3000 / 80 / 53 | 3100 / 3180 / 53@192.168.88.134 |
| portainer | portainer | 9000 / 9443 / 8000 | 同左 |

**data-plane 不在这个 daemon 上** ⇒ 该 endpoint 的容器不能使用"容器名"形态，只能用 host:发布端口。

## 结论（实现依据）

1. **stack → 容器**靠 `com.docker.compose.project` label 归属，`/api/stacks` 不含容器列表，必须由 containers 反查。stack `Type=2`（standalone compose），`EndpointId` 指向 daemon。
2. **容器名 DNS 只在 user-defined 网络上成立**；默认 `bridge` 无容器接入。实际可用的容器名形态示例如 `http://static-pages:8080`、`http://sid-idserver-1:5001`。
3. **禁用容器 IP 形态**：两个 daemon 的 10.89.x 子网重叠（EP2 adguardhome 与 EP4 sid-idserver 同为 `10.89.1.4`），IP 地址有歧义。
4. **host 形态**：published port 的绑定 IP 若为 `0.0.0.0` 则需用配置的宿主机地址（此处 `192.168.88.134`）；若为具体 IP（如 adguardhome 的 `53@192.168.88.134`）直接采用。
5. **自我排除对象**：`reverseproxy`（含 control-plane/data-plane，避免自代理环路）、`portainer`、`portainer-agent`；退出态容器一律排除。
6. **data-plane 归属判定**：在目标网络里找 compose project ∈ 自我 stack 且 service=`data-plane` 的容器。EP4/`default_bridge` = 有，EP2 = 无。前端据此启用/禁用"容器名"形态。
7. `/api/stacks` 无分页参数、无容器内联；`/api/endpoints/{id}/docker/...` 是唯一取容器/网络明细的通道（CE 可用）。
8. 所有业务容器均有发布端口，host 形态全量可用；aspire-dashboard 有多端口（4317/18888）——勾选矩阵按"容器 × 端口"设计。

## 第二实例：debian12（`http://100.102.18.111:9000`，2.39.3 CE）

| 项 | 值 |
|---|---|
| Endpoint | 1 个：Id=2 `local`，Type 1，**Docker 26.1.4**（非 Podman），unix socket |
| 规模 | 49 容器 / 19 网络 / snapshot 记录 32 stacks |
| `/api/stacks` | **只返回 12 个**——仅 Portainer 自己创建的 stack；容器 label 里有 **32 个命名 compose 项目**（另有 6 个无 label 容器），gitea、minio、redis、registry、rabbitmq 等均不在 API 内 |

### 与 debian13 的关键差异

1. **容器优先是硬要求**：`/api/stacks` 不完整，不能用它枚举应用；必须以容器的 `com.docker.compose.project` 分组。
2. **网络拓扑不同**：每个项目一张独立 bridge（`app-portal_default` 172.21.x、`bridge-local` 172.29.x、`mingo-reverseproxy_default` 172.27.x…），data-plane 只在 `mingo-reverseproxy_default` 中 ⇒ 绝大多数应用的"容器名"形态不可用，只能用 host 形态。
3. 容器 `NetworkSettings.Networks.*.Aliases` 为 `null`；容器名解析不能靠该字段判断，只能按"网络是否 user-defined + data-plane 是否同网"推导。
4. 发布端口均绑定 `0.0.0.0` ⇒ 地址中的 host 必须取自配置（无法从 endpoint URL 推导，因是 unix socket）。
5. 自我 stack 名为 `mingo-reverseproxy`（debian13 是 `reverseproxy`）⇒ `SelfStackNames` 必须是可配置列表，默认同时包含两者。
6. `/networks` 在 2.39 返回的 `Containers` 映射为空 ⇒ 网络归属只能从容器侧的 `NetworkSettings.Networks` 取。

## 自我识别（SelfStackNames 自动推导）

对三个 control-plane 容器做 inspect（只读，未读取 Env）：

| 部署 | Container Id 前缀 | `Config.Hostname` | compose project | 发布端口 |
|---|---|---|---|---|
| debian13 EP4 | `d69dc8afd9d4…` | `d69dc8afd9d4` | `reverseproxy` | 5000/5001 |
| debian12 | `11c3258b03d3…` | `11c3258b03d3` | `mingo-reverseproxy` | 5000/5001 |
| debian12（另一实例） | `3f8782a215bf…` | `3f8782a215bf` | `1` | **无发布端口** |

结论：**容器内 `Environment.MachineName` / `HOSTNAME` = 自身 Container Id 前 12 位**，可与发现结果中的 `Id` 前缀匹配，再读 `com.docker.compose.project` 得到自身 stack。三个部署全部成立，且不依赖 stack 名（`reverseproxy` / `mingo-reverseproxy` / `1` 各不相同）。

注意：debian12 上同时存在两个 MinGo 部署，硬编码名字会误伤或漏掉；按主机名匹配只会命中"自己"。第三个实例无发布端口，说明按端口猜测自我是行不通的。

## 原始响应

debian13（2.45.0）：
- `endpoints.json`
- `stacks.json`
- `containers.json` / `networks.json` / `info.json`（endpoint 2）
- `ep4_containers_json_all=1.json` / `ep4_networks.json` / `ep4_info.json`（endpoint 4）

debian12（2.39.3）：
- `d12/endpoints.json` / `d12/stacks.json` / `d12/stacks_limit100.json` / `d12/stacks_headers.txt`
- `d12/containers.json` / `d12/networks.json` / `d12/info.json`
