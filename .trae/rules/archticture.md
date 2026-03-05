# 目标

使用aspnetcore+YARP实现一个API网关及其控制平面

# 技术框架

- aspnetcore
- YARP
- Blazor
- Tailwindcss

# 禁止使用

- 存在漏洞的包或者方案
- 过时的，低效的方案
- 不能跨平台的方案

# 注意

- 项目运行前，需要先确保npm包正确使用pnpm还原

# 实现规范

## 遵循整洁架构/洋葱架构

- 实现层次为更具体的实现依赖更抽象的逻辑
- 分为业务Core、Application、Infrastructure、Web
  - Core：实体，DTO，共享工具类，基础设施能力的抽象，业务能力抽象，不依赖
  - Application: 业务逻辑组织，依赖Core，不会直接依赖具体的外部系统
  - Infrastructure: 仅依赖Core，与外部基础设施，如数据库、文件系统、网络等
  - Web: 依赖Application和Infrastructure，实现与外部系统的交互，如Web API、Blazor等

## 其他

- 使用模板创建项目时，默认代码或实现都需要移除，保持代码整洁
  