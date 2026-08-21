import { createSignal, For } from 'solid-js';
import { api } from '../api/client';

const alertStyles: Record<string, string> = {
  danger: 'border-l-4 border-danger bg-danger/5 dark:bg-danger/10 rounded-r-lg',
  warning: 'border-l-4 border-warning bg-warning/5 dark:bg-warning/10 rounded-r-lg',
  success: 'border-l-4 border-success bg-success/5 dark:bg-success/10 rounded-r-lg',
};

export default function MonitoringPage() {
  const [timeRange, setTimeRange] = createSignal('1h');
  const [alerts] = createSignal([
    { level: 'danger', title: '服务不可用', time: '2026-02-12 23:00', desc: '订单服务健康检查失败' },
    { level: 'warning', title: '速率限制触发', time: '2026-02-12 22:30', desc: 'IP 192.168.1.100 触发速率限制' },
    { level: 'success', title: '服务恢复', time: '2026-02-12 22:00', desc: '产品服务健康检查恢复正常' },
  ]);

  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">监控</h2>
        <p class="text-secondary">查看网关运行指标和告警</p>
      </div>

      <div class="card mb-6">
        <div class="flex items-center justify-between mb-4">
          <h3 class="font-semibold">系统指标</h3>
          <select class="input w-auto" value={timeRange()} onChange={(e) => setTimeRange(e.currentTarget.value)}>
            <option value="1h">最近1小时</option>
            <option value="6h">最近6小时</option>
            <option value="24h">最近24小时</option>
            <option value="7d">最近7天</option>
          </select>
        </div>
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {['请求量趋势', '响应时间趋势', '错误率趋势', '系统资源使用'].map((title) => (
            <div class="h-48 bg-gray-50 dark:bg-dark-200 rounded-lg flex items-center justify-center">
              <p class="text-secondary">{title}图表</p>
            </div>
          ))}
        </div>
      </div>

      <div class="card">
        <h3 class="font-semibold mb-4">告警记录</h3>
        <div class="space-y-4">
          <For each={alerts()}>{(alert) => (
            <div class={`p-4 ${alertStyles[alert.level] || alertStyles.warning}`}>
              <div class="flex items-center justify-between">
                <h4 class="font-medium">{alert.title}</h4>
                <span class="text-sm text-secondary">{alert.time}</span>
              </div>
              <p class="text-sm mt-1">{alert.desc}</p>
            </div>
          )}</For>
        </div>
      </div>
    </div>
  );
}
