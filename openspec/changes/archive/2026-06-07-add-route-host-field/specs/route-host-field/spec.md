## ADDED Requirements

### Requirement: 路由列表展示匹配域名

路由管理页面的表格中 SHALL 展示"匹配域名"列。

#### Scenario: 路由有 Host 时显示域名

- **WHEN** 路由的 `match.host` 有值
- **THEN** 表格中"匹配域名"列显示该域名

#### Scenario: 路由没有 Host 时显示占位符

- **WHEN** 路由的 `match.host` 为 null 或空字符串
- **THEN** 表格中"匹配域名"列显示 `-`

### Requirement: 路由表单可编辑匹配域名

路由编辑/新建对话框 SHALL 包含"匹配域名"输入框。

#### Scenario: 新建路由时填写域名

- **WHEN** 用户点击"添加路由"
- **THEN** 表单中显示"匹配域名"输入框，值为空

#### Scenario: 编辑已有域名路由

- **WHEN** 用户编辑已有 `host` 值的路由
- **THEN** "匹配域名"输入框预填当前域名值

#### Scenario: 保存时传递域名

- **WHEN** 用户填写了"匹配域名"并保存
- **THEN** 保存的 `match` 对象包含 `host` 字段

### Requirement: 搜索支持匹配域名

路由搜索功能 SHALL 同时匹配路由名称、路径和域名。

#### Scenario: 按域名搜索路由

- **WHEN** 用户在搜索框输入域名关键词
- **THEN** 匹配域名包含该关键词的路由被展示

### Requirement: 控制面增量广播传播 MatchHost

控制面配置增量广播 `BroadcastConfigUpdateAsync` SHALL 将路由的 `MatchHost` 字段传播到数据面。

#### Scenario: 增量配置更新包含域名

- **WHEN** 控制面执行增量配置广播
- **THEN** gRPC `RouteConfig` 消息的 `match_host` 字段被正确设置
