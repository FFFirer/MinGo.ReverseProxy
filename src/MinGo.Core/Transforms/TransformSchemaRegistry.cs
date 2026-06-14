namespace MinGo.Core.Transforms;

/// <summary>
/// 描述一个字段的 schema，供前端动态渲染配置表单
/// </summary>
public class TransformFieldSchema
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "text"; // "text" | "select"
    public List<string>? Options { get; set; }
    public bool Required { get; set; }
    public string? Placeholder { get; set; }
    public string? DefaultValue { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// 描述一个 transform 类型的完整 schema，包含它的字段定义和默认条目
/// </summary>
public class TransformSchema
{
    public string Type { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Order { get; set; }
    public bool IsList { get; set; }
    public List<TransformFieldSchema> Fields { get; set; } = new();
    public List<Dictionary<string, string>>? DefaultEntries { get; set; }
}

/// <summary>
/// 所有已知 transform 类型的静态注册表。
/// 前端通过 GET /api/transforms/schemas 获取这些 schema 来动态渲染配置表单。
/// 添加新 transform 只需要在此列表中增加一个条目。
/// </summary>
public static class TransformSchemaRegistry
{
    public static readonly IReadOnlyList<TransformSchema> Schemas = new List<TransformSchema>
    {
        // ── 路径重写 ──
        new()
        {
            Type = "PathPrefix",
            DisplayName = "路径前缀",
            Category = "path",
            Description = "在转发请求前添加或移除 URL 路径前缀",
            Order = 1,
            Fields = new()
            {
                new() { Key = "PathPrefix", Label = "添加前缀", Type = "text", Required = true, Placeholder = "/v1" },
                new() { Key = "Prefix", Label = "移除前缀 (可选)", Type = "text", Placeholder = "/api" },
            }
        },
        new()
        {
            Type = "PathPattern",
            DisplayName = "路径模式",
            Category = "path",
            Description = "使用模式匹配重写 URL 路径",
            Order = 2,
            Fields = new()
            {
                new() { Key = "PathPattern", Label = "模式", Type = "text", Required = true, Placeholder = "/{**remainder}" },
            }
        },

        // ── 请求头操作 ──
        new()
        {
            Type = "RequestHeader",
            DisplayName = "请求头",
            Category = "requestHeader",
            Description = "添加、设置或移除转发请求的 HTTP 头部",
            Order = 3,
            IsList = true,
            Fields = new()
            {
                new() { Key = "RequestHeader", Label = "Header 名", Type = "text", Required = true, Placeholder = "X-Custom" },
                new() { Key = "value", Label = "值", Type = "text" },
                new() { Key = "action", Label = "操作", Type = "select", Required = true,
                    Options = new() { "Set", "Append", "Remove" }, DefaultValue = "Set" },
            }
        },

        // ── 响应头操作 ──
        new()
        {
            Type = "ResponseHeader",
            DisplayName = "响应头",
            Category = "responseHeader",
            Description = "添加、设置或移除上游返回的 HTTP 头部",
            Order = 4,
            IsList = true,
            Fields = new()
            {
                new() { Key = "ResponseHeader", Label = "Header 名", Type = "text", Required = true, Placeholder = "X-Proxy" },
                new() { Key = "value", Label = "值", Type = "text" },
                new() { Key = "action", Label = "操作", Type = "select", Required = true,
                    Options = new() { "Set", "Append", "Remove" }, DefaultValue = "Set" },
            }
        },

        // ── X-Forwarded 头控制 ──
        new()
        {
            Type = "XForwarded",
            DisplayName = "X-Forwarded",
            Category = "xForwarded",
            Description = "控制 YARP 添加 X-Forwarded-For/Proto/Host/Prefix 头的行为",
            Order = 5,
            IsList = true,
            DefaultEntries = new()
            {
                new() { { "XForwarded", "For" }, { "action", "Set" } },
                new() { { "XForwarded", "Proto" }, { "action", "Set" } },
                new() { { "XForwarded", "Host" }, { "action", "Set" } },
            },
            Fields = new()
            {
                new() { Key = "XForwarded", Label = "头部", Type = "select", Required = true,
                    Options = new() { "For", "Proto", "Host", "Prefix" } },
                new() { Key = "action", Label = "操作", Type = "select", Required = true,
                    Options = new() { "Set", "Append", "Clear" }, DefaultValue = "Set" },
            }
        },
    };
}
