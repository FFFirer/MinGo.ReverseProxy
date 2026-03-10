using System.Collections.Concurrent;
using System.Diagnostics;

namespace MinGo.Core.Services
{
    /// <summary>
    /// 遥测数据存储
    /// </summary>
    public class TelemetryStore
    {
        /// <summary>
        /// 最大跟踪数量
        /// </summary>
        private const int MaxTraces = 2000;

        /// <summary>
        /// 跟踪数据队列
        /// </summary>
        public ConcurrentQueue<Activity> Traces = new();

        /// <summary>
        /// 指标数据字典
        /// </summary>
        public ConcurrentDictionary<string, List<MetricPoint>> Metrics = new();

        /// <summary>
        /// 日志数据环形缓冲区
        /// </summary>
        public ConcurrentQueue<string> Logs = new();

        /// <summary>
        /// 添加跟踪数据
        /// </summary>
        /// <param name="activity">活动对象</param>
        public void AddTrace(Activity activity)
        {
            Traces.Enqueue(activity);
            if (Traces.Count > MaxTraces)
            {
                Traces.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 添加指标数据
        /// </summary>
        /// <param name="name">指标名称</param>
        /// <param name="point">指标点</param>
        public void AddMetric(string name, MetricPoint point)
        {
            if (!Metrics.TryGetValue(name, out var points))
            {
                points = new List<MetricPoint>();
                Metrics[name] = points;
            }
            
            points.Add(point);
            // 限制指标数据点数量（24小时）
            var cutoffTime = DateTime.UtcNow.AddHours(-24);
            points.RemoveAll(p => p.Timestamp < cutoffTime);
        }

        /// <summary>
        /// 添加日志数据
        /// </summary>
        /// <param name="log">日志内容</param>
        public void AddLog(string log)
        {
            Logs.Enqueue(log);
            if (Logs.Count > 10000)
            {
                Logs.TryDequeue(out _);
            }
        }
    }

    /// <summary>
    /// 指标点
    /// </summary>
    public class MetricPoint
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 值
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// 标签
        /// </summary>
        public Dictionary<string, object> Tags { get; set; } = new();
    }
}