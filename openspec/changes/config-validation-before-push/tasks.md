## 1. 校验结果模型

- [ ] 1.1 创建 MinGo.Core/Validation/ConfigValidationError.cs - 定义错误模型（Code, Message, EntityId, EntityType）
- [ ] 1.2 创建 MinGo.Core/Validation/ValidationResult.cs - 定义校验结果模型（IsValid, Errors, Warnings）

## 2. ConfigValidator 实现

- [ ] 2.1 创建 MinGo.Core/Validation/ConfigValidator.cs - 主类，定义 Validate(ConfigSnapshot) 方法
- [ ] 2.2 实现 ValidateRoutes() - 校验 Route ID、ClusterId 非空
- [ ] 2.3 实现 ValidateRouteClusterReferences() - 校验 Route.ClusterId 引用的 Cluster 存在
- [ ] 2.4 实现 ValidateRouteTransforms() - 校验 TransformsJson 格式
- [ ] 2.5 实现 ValidateClusters() - 校验 Cluster ID 非空、Destinations 非空、LB Policy 合法
- [ ] 2.6 实现 ValidateDestinations() - 校验 Destination ID、Address 非空、URL 格式
- [ ] 2.7 实现 ValidateCertificates() - 校验 DomainName、CertificateBytes、Thumbprint 非空
- [ ] 2.8 实现 ValidateCertificateExpiry() - 证书过期检查（Warning 级别）

## 3. ConfigReplicationService 集成

- [ ] 3.1 在 ConfigReplicationService 中注入 ConfigValidator
- [ ] 3.2 修改 BuildConfigSnapshotAsync() - 构建 snapshot 后调用 Validate()
- [ ] 3.3 修改 BroadcastConfigUpdateAsync() - 构建 snapshot 后调用 Validate()
- [ ] 3.4 实现校验失败处理 - 记录错误日志，不推送配置

## 4. 日志与可观测性

- [ ] 4.1 配置校验失败时记录 Error 级别日志，包含所有错误详情
- [ ] 4.2 配置校验通过但有 Warning 时记录 Warning 级别日志

## 5. 测试

- [ ] 5.1 创建 ConfigValidator 单元测试 - 测试所有校验规则
- [ ] 5.2 测试校验通过场景 - 有效配置返回 IsValid=true
- [ ] 5.3 测试校验失败场景 - 各种无效配置返回对应错误码
