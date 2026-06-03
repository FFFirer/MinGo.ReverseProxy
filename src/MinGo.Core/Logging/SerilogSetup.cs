using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace MinGo.Core.Logging;

/// <summary>
/// 共享的 Serilog 配置逻辑
/// DataPlane 和 ControlPlane 通过此方法统一日志配置
/// </summary>
public static class SerilogSetup
{
    /// <summary>
    /// 获取共享的 Serilog LoggerConfiguration 委托
    /// 各项目在 Program.cs 中:
    ///   builder.Host.UseSerilog(SerilogSetup.ConfigureSharedSerilog());
    /// </summary>
    public static Action<HostBuilderContext, LoggerConfiguration> ConfigureSharedSerilog()
    {
        return (ctx, cfg) =>
        {
            cfg.ReadFrom.Configuration(ctx.Configuration);

            // 抑制 ASP.NET Core 框架级的 Information 日志
            cfg.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning);

            // 抑制 YARP 反向代理内部诊断日志
            cfg.MinimumLevel.Override("Yarp", LogEventLevel.Warning);

            cfg.Enrich.FromLogContext();
        };
    }
}
