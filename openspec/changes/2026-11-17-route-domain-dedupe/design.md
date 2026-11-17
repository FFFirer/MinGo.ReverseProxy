## Context

「按域名去重」听起来是一句校验，落到这套架构里牵出三个问题：匹配键怎么算才算「同一个」；闸门放在哪一层；隧道 → 数据面这一跳如何在底层配置和图上都被承认。

## Decisions

### 1. 匹配键按「域名 + 路径」，不按域名单独判重

同域名不同路径（`/api/*` 与 `/admin/*`）是反向代理的常态，只按域名判重会把网关废成「一个域名一条规则」。因此键是 `(Host, Path)` 二元组，用户诉求里的「根据域名去重」在这个口径下成立：同域名 + 同路径才算重复。

通配域名与精确域名（`*.example.com` 与 `api.example.com`）**不算**重复。

### 2. 归一化口径以实测的 YARP 行为为准，而不是凭直觉

用 `tests/MinGo.DataPlane.Tests/YarpMatchPrecedenceTests.cs`（HostBuilder + Kestrel + `LoadFromMemory`，三个 mock 后端）实测得到：

| 假设 | 实测结论 |
| --- | --- |
| 精确域名与 `*.example.com` 同时命中 | 精确域名优先，通配是兜底层 |
| 域名匹配大小写 | 不敏感（`API.EXAMPLE.COM` 命中 `api.example.com`） |
| 路径字面量大小写 | 不敏感（`/V1/*` 与 `/v1/*` 等价） |
| 尾点 | `api.example.com.` 与 `api.example.com` 等价 |

由此定下 `RouteMatchKey`：域名 `Trim().TrimEnd('.').ToLowerInvariant()`，空值归一为 `*`；路径 `Trim().ToLowerInvariant()`，空值与 `/*`、`/**`、`/{**catch-all}`、`/{**catchall}` 归一为「任意路径」。这套归一化与 `IngressReconciler.NormalizeHost` **不能互换**——后者空串表示「不产出 hostname 规则」，语义不同。

### 3. 闸门放保存期，行为是 400 拒绝

拓扑页的 `duplicate-match` 只体检不阻断，歧义规则已经躺在库里了。拦在创建/更新接口上，用户在唯一能低成本改掉的时刻看到提示。不选「自动停用旧的」：静默改变另一条路由的生效状态比报错更危险。

### 4. 不造影子路由，而是显式编译下发路由

一种做法是给隧道路由自动补一条 `ConnectionId=local` 的记录，但那会让库里凭空多出一批同域名行，反而加剧重复。选择让 `DataPlaneRuleCompiler` 在生成快照时显式编译：启用路由 → 数据面规则，同键取一条（ID 字典序，结果确定），其余记 Warning。

契约的守卫方式是测试而非注释：`DataPlaneRuleCompilerTests.Compile_KeepsTunnelBoundRoutes` 断言绑定隧道的启用路由必须出现在编译结果里。

### 5. 拓扑图承认中转跳

原图把 `conn:{tunnel} → route` 直接连上，读者会以为隧道自己找到了后端。改成 `conn:{tunnel} → conn:local`（origin）+ `conn:local → route`（ingress）。数据面节点缺失（老通道清单）时退回直连，避免路由悬空——这是图形降级，不代表行为改变。

## Risks

- 存量库里若已有同键启用路由，闸门不影响读取，但下次编辑任一条都会被拦。缓解：错误信息直接给出占用方名字与「直接编辑已有那条路由」的出路，拓扑页也能定位。
- 前端 `Routes.tsx` 里复制了一份归一化逻辑做实时提示，存在与后端漂移的风险。缓解：函数旁写明「与 `src/MinGo.Core/Routing/RouteMatchKey.cs` 同一口径，改动必须同步」，且后端始终是唯一权威判定；前端漏判只会退化成 400，不会放过重复。
