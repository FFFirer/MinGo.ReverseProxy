namespace MinGo.Core.Services;

/// <summary>
/// 网关请求指标名的唯一权威定义。
/// 采集侧（数据面 instruments 与 store 写入）与查询侧（控制面 MonitoringService、遥测端点）
/// 都从这里取名字 —— 字符串各写一遍时，改一个漏一个不会编译报错，只会让某个看板静默变空。
/// 自观测计数指标名（驱逐、未上报丢弃）属于存储层，定义在 <see cref="TelemetryStore"/> 上。
/// </summary>
public static class GatewayMetricNames
{
    /// <summary>每请求一个点，点值恒为 1；对值求和即请求总数</summary>
    public const string RequestsTotal = "gateway.requests.total";

    /// <summary>网关侧失败（5xx 与管道异常），点值恒为 1</summary>
    public const string RequestsErrors = "gateway.requests.errors";

    /// <summary>每请求一个点，点值是该请求在网关边缘的毫秒耗时</summary>
    public const string RequestDuration = "gateway.request.duration";
}
