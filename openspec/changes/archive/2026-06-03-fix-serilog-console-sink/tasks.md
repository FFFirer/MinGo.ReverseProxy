## 1. 配置修改

- [x] 1.1 在 `src/MinGo.ControlPlane.Api/appsettings.json` 的 `Serilog` 节中新增 `WriteTo` Console sink
- [ ] 1.2 本地构建并验证：`podman compose build control-plane && podman compose up` 确认日志正常输出
