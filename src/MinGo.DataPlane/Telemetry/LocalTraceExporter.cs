using System.Diagnostics;
using OpenTelemetry;
using MinGo.Core.Services;

namespace MinGo.DataPlane.Telemetry;

/// <summary>
/// 把结束的 Activity 落到数据面自己的 <see cref="TelemetryStore"/>。
/// 补齐 docs/基本可观测架构.md 第六节设计却从未实现的本地 exporter：
/// 在此之前 <c>TelemetryStore.AddTrace</c> 没有任何调用方。
/// 注意这条队列目前只有写入方：心跳协议不携带 trace，控制面
/// <c>GET /api/telemetry/traces</c> 读的是控制面进程内的另一个 store 实例，
/// 因此跨进程的链路查询要到上报协议补齐后才成立（阶段三）。
/// 链路的对外可见性当前由同一条 pipeline 上的 OTLP exporter 承担。
/// </summary>
public sealed class LocalTraceExporter : BaseExporter<Activity>
{
    private readonly TelemetryStore _store;

    public LocalTraceExporter(TelemetryStore store)
    {
        _store = store;
    }

    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
        {
            _store.AddTrace(activity);
        }

        return ExportResult.Success;
    }
}
