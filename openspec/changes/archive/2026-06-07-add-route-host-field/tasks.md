## 1. 后端 Bug 修复

- [x] 1.1 修复 `ConfigReplicationService.cs` 的 `BroadcastConfigUpdateAsync` 方法，补全 `MatchHost = route.Match?.Host ?? ""`

## 2. 前端表格增加"匹配域名"列

- [x] 2.1 在路由表格的 `<th>` 中"路径"和"集群"之间插入"匹配域名"列头
- [x] 2.2 在路由表格的 `<td>` 中对应位置展示 `route.match?.host || '-'`
- [x] 2.3 更新表格 colspan（空状态和加载中的 `colspan` 从 5 改为 6）

## 3. 前端表单增加"匹配域名"输入框

- [x] 3.1 在 `RouteFormModal` 中添加 `host` 信号量 `const [host, setHost] = createSignal(props.route?.match?.host || '')`
- [x] 3.2 在"匹配路径"和"目标集群"之间插入"匹配域名"输入框
- [x] 3.3 保存时在 `match` 对象中包含 `host: host() || undefined`

## 4. 前端搜索支持域名

- [x] 4.1 搜索过滤逻辑中增加 `r.match?.host?.includes(search())`

## 5. 验证

- [x] 5.1 确认前端编译无报错
- [x] 5.2 确认后端编译无报错
- [x] 5.3 确认增量广播补全未引入新问题
