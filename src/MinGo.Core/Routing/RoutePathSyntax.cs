using Microsoft.AspNetCore.Routing.Patterns;

namespace MinGo.Core.Routing;

/// <summary>
/// 匹配路径语法校验，用与 YARP 相同的路由模板解析器（RoutePatternFactory）。
/// 一条语法非法的路径（如缺右括号的 <c>/{**catch-all)</c>）会让 YARP 拒绝整份代理配置
/// （实测：<c>Unable to load or apply the proxy configuration</c>），数据面全部路由随之 404，
/// 所以必须拦在保存期与数据面快照校验，而不是等 YARP 静默吞掉。
/// </summary>
public static class RoutePathSyntax
{
    /// <summary>空值视为「不限路径」，合法。</summary>
    public static bool IsValid(string? path, out string? error)
    {
        error = null;
        var value = (path ?? string.Empty).Trim();
        if (value.Length == 0)
            return true;

        if (!value.StartsWith('/'))
        {
            error = "路径必须以 / 开头";
            return false;
        }

        try
        {
            RoutePatternFactory.Parse(value);
            return true;
        }
        catch (RoutePatternException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
