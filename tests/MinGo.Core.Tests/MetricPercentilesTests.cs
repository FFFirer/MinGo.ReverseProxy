using MinGo.Core.Services;

namespace MinGo.Core.Tests;

/// <summary>
/// 单元验证：分位计算工具
/// 契约见 request-telemetry 规范的总览分位要求——总览与时序必须同口径，
/// 因此两者共用这里的同一实现
/// </summary>
public class MetricPercentilesTests
{
    private static readonly IEnumerable<double> FirstHundred = Enumerable.Range(1, 100).Select(i => (double)i);

    [Fact]
    public void EmptySamples_ReturnZero()
    {
        Assert.Equal(0, MetricPercentiles.NearestRank(Array.Empty<double>(), 95));
        Assert.Equal(0, MetricPercentiles.NearestRank(null!, 95));
    }

    [Fact]
    public void NearestRank_MatchesOriginalMonitoringServiceFormula()
    {
        // 1..100 时 nearest-rank 的取值就是 rank 本身
        Assert.Equal(50d, MetricPercentiles.NearestRank(FirstHundred, 50));
        Assert.Equal(95d, MetricPercentiles.NearestRank(FirstHundred, 95));
        Assert.Equal(99d, MetricPercentiles.NearestRank(FirstHundred, 99));
        Assert.Equal(100d, MetricPercentiles.NearestRank(FirstHundred, 100));
    }

    [Fact]
    public void OrderOfSamples_DoesNotAffectResult()
    {
        var shuffled = new double[] { 40, 2, 9, 77, 15, 3, 61 };
        var sorted = shuffled.OrderBy(v => v).ToArray();

        Assert.Equal(
            MetricPercentiles.NearestRank(sorted, 90),
            MetricPercentiles.NearestRank(shuffled, 90));
    }

    [Fact]
    public void SingleSample_AllPercentilesCollapseToIt()
    {
        Assert.Equal(12.5d, MetricPercentiles.NearestRank([12.5], 50));
        Assert.Equal(12.5d, MetricPercentiles.NearestRank([12.5], 99));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(100)]
    public void HigherPercentile_IsNeverBelowLowerOne(int count)
    {
        var samples = Enumerable.Range(1, count).Select(i => (double)i);

        var p50 = MetricPercentiles.NearestRank(samples, 50);
        var p95 = MetricPercentiles.NearestRank(samples, 95);
        var p99 = MetricPercentiles.NearestRank(samples, 99);

        Assert.True(p50 <= p95, $"样本 {count} 个：P50={p50} 大于 P95={p95}");
        Assert.True(p95 <= p99, $"样本 {count} 个：P95={p95} 大于 P99={p99}");
    }

    /// <summary>
    /// nearest-rank 在保留窗口受限时是近似值：它取的是样本中的实际观测值，
    /// 不做插值。锁定这个行为，避免后续误当成精确分位使用
    /// </summary>
    [Fact]
    public void NearestRank_ReturnsObservedValue_NotInterpolated()
    {
        var samples = new double[] { 10, 20 };

        Assert.Equal(20d, MetricPercentiles.NearestRank(samples, 99));
    }
}
