using System.Diagnostics;

namespace MinGo.DataPlane.Heartbeat;

/// <summary>
/// 一次进程占用采样的结果。
/// <see cref="CpuPercent"/> 是采样区间内的平均 CPU 占用百分比（0-100，多核归一化），
/// <see cref="MemoryMegabytes"/> 是进程工作集，单位 MB —— proto 里的 memory_usage 是裸 double，
/// 单位必须在类型与文档里钉死，否则前端的"0.5"既可能是 50% 也可能是 0.5 MB。
/// </summary>
public readonly record struct ProcessUsage(double CpuPercent, double MemoryMegabytes);

/// <summary>
/// 进程占用采样接缝。
/// 心跳上报要能断言"CPU 与内存是算出来的"，而 Process / Environment 的静态读取喂不进假数据，
/// 因此把取值包一层：生产用 <see cref="ProcessSampler"/>，测试注入固定值。
/// </summary>
public interface IProcessSampler
{
    /// <summary>返回自上次采样以来的 CPU 占用与当前工作集</summary>
    ProcessUsage Sample();
}

/// <summary>
/// 基于托管 API 的进程采样实现。
/// 不用 System.Diagnostics.PerformanceCounter：它需要额外的包且只覆盖 Windows，
/// 而 TotalProcessorTime / WorkingSet 在 Linux 容器里同样可用。
/// </summary>
public sealed class ProcessSampler : IProcessSampler, IDisposable
{
    private const double Megabyte = 1024 * 1024;

    private readonly Process _process = Process.GetCurrentProcess();
    private readonly object _gate = new();

    private TimeSpan _lastCpuTime;
    private long _lastTimestamp;
    private bool _hasPreviousSample;

    public ProcessUsage Sample()
    {
        var cpuTime = _process.TotalProcessorTime;
        var timestamp = Stopwatch.GetTimestamp();

        lock (_gate)
        {
            double cpuPercent;

            if (_hasPreviousSample)
            {
                var wallSeconds = Stopwatch.GetElapsedTime(_lastTimestamp, timestamp).TotalSeconds;
                var cpuSeconds = (cpuTime - _lastCpuTime).TotalSeconds;
                cpuPercent = Percentage(cpuSeconds, wallSeconds);
            }
            else
            {
                // 首拍没有区间可算。用进程启动以来的平均值兜底，
                // 报 0 会被读成"空闲"，那是另一种形式的假数据。
                _hasPreviousSample = true;
                var wallSeconds = (DateTime.UtcNow - _process.StartTime.ToUniversalTime()).TotalSeconds;
                cpuPercent = Percentage(cpuTime.TotalSeconds, wallSeconds);
            }

            _lastCpuTime = cpuTime;
            _lastTimestamp = timestamp;

            return new ProcessUsage(cpuPercent, ReadMemoryMegabytes());
        }
    }

    /// <summary>
    /// 区间 CPU 占比：进程 CPU 时间除以"核数 × 墙钟时间"，钳位到 [0, 100]。
    /// 钳位是必要的：调度抖动与容器 CPU 配额都能算出略大于 100 的值。
    /// </summary>
    private static double Percentage(double cpuSeconds, double wallSeconds)
    {
        if (wallSeconds <= 0)
        {
            return 0;
        }

        var denominator = Math.Max(1, Environment.ProcessorCount) * wallSeconds;
        var percent = cpuSeconds / denominator * 100.0;

        return percent switch
        {
            < 0 => 0,
            > 100 => 100,
            _ => percent
        };
    }

    private double ReadMemoryMegabytes()
    {
        // Environment.WorkingSet 是首选（含共享页与私有脏页的更贴近计费口径），
        // 个别平台上返回 0，回退到进程对象的 WorkingSet64
        var bytes = Environment.WorkingSet;
        if (bytes <= 0)
        {
            bytes = _process.WorkingSet64;
        }

        return bytes / Megabyte;
    }

    public void Dispose() => _process.Dispose();
}
