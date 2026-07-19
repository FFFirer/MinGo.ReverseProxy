## 1. GatewayInstanceService 内存实现

- [ ] 1.1 重写 `GatewayInstanceService`：`ConcurrentDictionary<string, GatewayInstance>` 存储，实现所有 7 个方法
- [ ] 1.2 `GetInstancesAsync` 实现超时检测（30s→HeartbeatTimeout，120s→Offline）和响应组装
- [ ] 1.3 `RegisterInstanceAsync` 实现：生成 ID、初始化实例、写入字典
- [ ] 1.4 `UpdateHeartbeatAsync` 实现：更新 CPU/内存/请求数/心跳时间
- [ ] 1.5 `RemoveInstanceAsync` / `GetInstanceAsync` 实现
- [ ] 1.6 `CheckAndUpdateTimeoutInstancesAsync` / `CleanupLongTimeTimeoutInstancesAsync` 实现
- [ ] 1.7 将 `Program.cs` 中 `IGatewayInstanceService` 注册从 Scoped 改为 Singleton

## 2. gRPC 服务联动

- [ ] 2.1 `HeartbeatCollectService` 注入 `IGatewayInstanceService`，心跳到来时调用 `UpdateHeartbeatAsync`
- [ ] 2.2 `ConfigReplicationService` 注入 `IGatewayInstanceService`，数据面连接时注册实例，断开时标记 Offline

## 3. 前端展示增强

- [ ] 3.1 `Instances.tsx` 表格增加列：CPU、内存、请求数、错误数、错误率、运行时长
- [ ] 3.2 添加删除按钮（带确认），调用 `DELETE /api/instances/{id}`
- [ ] 3.3 补充前端类型（如果现有类型缺少字段）
- [ ] 3.4 优化空状态和加载状态展示
