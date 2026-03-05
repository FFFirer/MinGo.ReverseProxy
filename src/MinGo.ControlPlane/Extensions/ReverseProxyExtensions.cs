using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy; 
using MinGo.ControlPlane.Services;
using MinGo.Shared.Data;
using Microsoft.Extensions.DependencyInjection;

namespace MinGo.ControlPlane.Extensions;

/// <summary>
/// YARP反向代理扩展方法
/// </summary>
public static class ReverseProxyExtensions
{
    /// <summary>
    /// 从数据库加载反向代理配置
    /// </summary>
    /// <param name="builder">反向代理构建器</param>
    /// <returns>反向代理构建器</returns>
    public static IReverseProxyBuilder LoadFromDatabase(this IReverseProxyBuilder builder)
    {
        // 注册数据库配置提供程序
        builder.Services.AddSingleton<DatabaseProxyConfigProvider>();
        builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DatabaseProxyConfigProvider>());
        
        return builder;
    }
}
