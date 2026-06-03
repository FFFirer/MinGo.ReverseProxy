## Context

ControlPlane.Api 的 `Properties/` 目录存在但为空。其他项目（如 `MinGo.ReverseProxy`）已有 `launchSettings.json` 参考模式。

## Goals / Non-Goals

**Goals:**
- 支持 `dotnet run` 直接启动 API
- 端口与 `appsettings.json` 的 Kestrel 配置一致（5000）
- 自动设置 `ASPNETCORE_ENVIRONMENT=Development`

**Non-Goals:**
- 不改动 gRPC 端口（5001，由 Kestrel 配置自动监听）
- 不添加 https profile（API 在开发环境只需 http）

## Decisions

参考 `MinGo.ReverseProxy/Properties/launchSettings.json` 的模式：

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5000",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

## Risks / Trade-offs

- 无风险。纯开发者工具配置。
