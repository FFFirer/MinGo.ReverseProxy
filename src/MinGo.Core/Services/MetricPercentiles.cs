namespace MinGo.Core.Services
{
    /// <summary>
    /// 指标分位计算工具
    /// 网关内所有对外暴露的延迟分位数都走同一实现，
    /// 避免总览与时间序列用不同算法给出互相冲突的 P95/P99
    /// </summary>
    public static class MetricPercentiles
    {
        /// <summary>
        /// 用 nearest-rank 法计算百分位数
        /// </summary>
        /// <param name="values">样本值，可为空</param>
        /// <param name="percentile">百分位，取值 (0, 100]</param>
        /// <returns>对应分位值；无样本时返回 0</returns>
        public static double NearestRank(IEnumerable<double> values, int percentile)
        {
            if (values is null)
            {
                return 0;
            }

            var sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count == 0)
            {
                return 0;
            }

            var index = (int)Math.Ceiling((percentile / 100.0) * sorted.Count) - 1;
            index = Math.Max(0, Math.Min(sorted.Count - 1, index));
            return sorted[index];
        }
    }
}
