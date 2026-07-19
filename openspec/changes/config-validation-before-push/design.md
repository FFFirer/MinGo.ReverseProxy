## Context

当前配置同步流程：Control Plane 从数据库加载 Route、Cluster、Certificate，构建 ConfigSnapshot，通过 gRPC 推送到所有 Data Plane。Data Plane 收到后直接应用到 YARP。

问题：数据库中的无效配置（如 Route 引用不存在的 Cluster、空地址的 Destination、无效的 LB Policy）会被直接推送到 Data Plane，导致：
- YARP 路由匹配失败
- 运行时 502/503 错误
- Data Plane 启动时配置加载异常

现有代码中 DataPlaneConfigProvider.ApplyConfig() 有 `catch { }` 吞掉 TransformsJson 解析错误，但不做任何校验。

## Goals / Non-Goals

**Goals:**
- 在 Control Plane 推送配置前校验 ConfigSnapshot 的合法性
- 校验失败时拒绝推送，Data Plane 继续使用旧配置
- 校验规则覆盖：Route→Cluster 引用、Destination 地址格式、LB Policy、TransformsJson、证书有效性
- 校验器放在 MinGo.Core，可被 Control Plane 和 Data Plane 复用

**Non-Goals:**
- 不修改 Data Plane 的配置接收逻辑（信任 Control Plane 的校验结果）
- 不支持校验规则的白名单/例外机制
- 不修改数据库写入时的校验（那是另一个能力）
- 不实现配置的增量校验（当前只做全量校验）

## Decisions

### Decision 1: 校验位置 - Control Plane 推送前

**选择**：在 ConfigReplicationService.BuildConfigSnapshotAsync() 和 BroadcastConfigUpdateAsync() 中校验

**替代方案**：
- Data Plane 接收时校验 → 问题：无效配置已到达 Data Plane，且每个 Data Plane 重复校验
- 数据库写入时校验 → 问题：不影响已存在的无效数据

**理由**：Control Plane 是配置推送的唯一入口，校验一次即可保护所有 Data Plane。

### Decision 2: 校验器放置 - MinGo.Core

**选择**：放在 MinGo.Core/Validation/ 目录

**替代方案**：
- MinGo.ControlPlane.Api → 问题：Data Plane 无法复用
- MinGo.Infrastructure → 问题：校验是业务逻辑，不是基础设施

**理由**：校验规则是核心业务逻辑，与具体技术实现无关。

### Decision 3: 失败策略 - 拒绝推送

**选择**：校验失败时记录错误日志，不推送配置

**替代方案**：
- 降级应用（移除无效部分）→ 问题：可能导致路由缺失，行为不可预测
- 尝试自动修复 → 问题：修复逻辑复杂，可能引入新问题

**理由**：拒绝推送是最安全的策略，保证线上配置始终有效。

### Decision 4: LB Policy 校验 - 硬编码白名单

**选择**：硬编码 YARP 支持的策略列表

```csharp
private static readonly HashSet<string> ValidLoadBalancingPolicies = new(StringComparer.OrdinalIgnoreCase)
{
    "RoundRobin", "Random", "LeastRequests", "PowerOfTwoChoices", "LeastConnection"
};
```

**替代方案**：
- 从 YARP 运行时获取 → 问题：增加依赖，且策略列表相对稳定

**理由**：YARP 支持的策略列表稳定，硬编码简单可靠。

### Decision 5: 证书过期 - Warning 而非 Error

**选择**：证书过期仅记录 Warning，不阻断推送

**理由**：过期证书可能仍有兼容性需求（如测试环境），且证书管理是独立关注点。

## Risks / Trade-offs

| Risk | Mitigation |
|------|------------|
| 现有数据库中可能存在无效配置，升级后会被拦截 | 提供校验报告工具，帮助识别和修复无效配置 |
| 校验逻辑可能遗漏某些边界情况 | 从简单规则开始，后续根据实际问题补充 |
| 同步校验可能影响推送延迟 | 校验逻辑轻量，延迟可忽略（<10ms） |
| ConfigValidator 依赖 Proto 类型，版本升级可能破坏兼容性 | 使用接口抽象，或在 Core 中定义独立的校验输入模型 |
