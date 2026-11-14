using System.Diagnostics;

namespace MinGo.DataPlane.Telemetry;

/// <summary>
/// 单请求的分段计时收集器：TTFB 由响应头回调标记，传输段由包装流标记
/// </summary>
public sealed class ResponseTiming
{
    private long _ttfbMs;
    private long _firstWriteMs = -1;
    private long _lastWriteMs;

    public long TtfbMs => Interlocked.Read(ref _ttfbMs);
    public long DownloadMs => _firstWriteMs < 0 ? 0 : Math.Max(0, Interlocked.Read(ref _lastWriteMs) - _firstWriteMs);

    public void MarkHeaders(long elapsedMs) => Interlocked.Exchange(ref _ttfbMs, elapsedMs);

    public void MarkWrite(long elapsedMs)
    {
        Interlocked.CompareExchange(ref _firstWriteMs, elapsedMs, -1);
        Interlocked.Exchange(ref _lastWriteMs, elapsedMs);
    }
}

/// <summary>
/// 包装响应体流，仅记录首/末次写入时间，其余全部委托给内层流
/// </summary>
public sealed class TimingResponseBodyStream : Stream
{
    private readonly Stream _inner;
    private readonly ResponseTiming _timing;
    private readonly Stopwatch _sw;

    public TimingResponseBodyStream(Stream inner, ResponseTiming timing, Stopwatch sw)
    {
        _inner = inner;
        _timing = timing;
        _sw = sw;
    }

    private void Mark() => _timing.MarkWrite(_sw.ElapsedMilliseconds);

    public override void Write(byte[] buffer, int offset, int count)
    {
        Mark();
        _inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Mark();
        _inner.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Mark();
        return _inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Mark();
        return _inner.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        Mark();
        _inner.WriteByte(value);
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => _inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        // 内层流由服务器持有生命周期，此处不代为释放
        base.Dispose(disposing);
    }
}
