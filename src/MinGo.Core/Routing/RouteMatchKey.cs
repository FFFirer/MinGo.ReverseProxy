namespace MinGo.Core.Routing;

/// <summary>
/// 路由匹配键：域名 + 路径的归一化结果，回答「这两条路由在底层是不是同一条规则」。
/// 保存期去重校验、底层配置编译、拓扑体检共用这一把尺子，避免三处口径各写一遍。
/// </summary>
/// <remarks>
/// 归一化规则来自 YARP 实测（<c>tests/MinGo.DataPlane.Tests/YarpMatchPrecedenceTests.cs</c>），不是凭印象定的：
/// <list type="bullet">
/// <item>域名与路径字面量都是大小写不敏感匹配，所以两者统一转小写、去尾部点；</item>
/// <item>通配 <c>*.example.com</c> 与具体 <c>api.example.com</c> 是两条不同规则，精确域名优先命中，
/// 因此它们<strong>不算</strong>重复，允许共存；</item>
/// <item>空路径与 YARP 的 catch-all 写法（<c>/{**catch-all}</c> 等）语义相同，都归一为
/// <see cref="AnyPath"/>，否则「不限路径」和「catch-all」会漏出去重。</item>
/// </list>
/// </remarks>
public readonly record struct RouteMatchKey(string Host, string Path)
{
    /// <summary>任意域名：未配置 <c>match.host</c> 的路由等价于此。</summary>
    public const string AnyHost = "*";

    /// <summary>任意路径：空路径与 catch-all 写法等价于此。</summary>
    public const string AnyPath = "*";

    public static RouteMatchKey Of(string? host, string? path) => new(NormalizeHost(host), NormalizePath(path));

    /// <summary>可读形式，用于提示与日志文案。</summary>
    public override string ToString() => $"{Host}{(Path == AnyPath ? string.Empty : Path)}";

    /// <summary>
    /// 域名归一化：去空白、去尾部点、转小写；空值归一为 <see cref="AnyHost"/>。
    /// 通配前缀 <c>*.</c> 原样保留，与具体域名区分开。
    /// </summary>
    public static string NormalizeHost(string? host)
    {
        var value = (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        return value.Length == 0 ? AnyHost : value;
    }

    /// <summary>
    /// 路径归一化：去空白、转小写、补前导斜杠；空值与 catch-all 写法归一为 <see cref="AnyPath"/>。
    /// </summary>
    public static string NormalizePath(string? path)
    {
        var value = (path ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0 || value is "/*" or "/**" or "/{**catch-all}" or "/{**catchall}"
                or "*" or "{**catch-all}" or "{**catchall}")
        {
            return AnyPath;
        }

        return value.StartsWith('/') ? value : "/" + value;
    }
}
