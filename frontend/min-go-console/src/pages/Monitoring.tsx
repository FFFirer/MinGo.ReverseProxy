import { createSignal, onMount, For, Show } from 'solid-js';
import { api } from '../api/client';
import { addToast } from '../store/toast';
import type { MetricsSummary, ServiceMetrics, ErrorMetrics } from '../types';

export default function MonitoringPage() {
  const [timeRange, setTimeRange] = createSignal('1h');
  const [metrics, setMetrics] = createSignal<MetricsSummary | null>(null);
  const [services, setServices] = createSignal<ServiceMetrics[]>([]);
  const [errors, setErrors] = createSignal<ErrorMetrics[]>([]);

  const fetchAll = async () => {
    try {
      const m = await api.get<MetricsSummary>('/monitoring/metrics');
      setMetrics(m);
    } catch { addToast('error', '加载指标失败'); }

    try {
      const s = await api.get<ServiceMetrics[]>('/monitoring/services');
      setServices(s || []);
    } catch { /* ignore */ }

    try {
      const end = new Date().toISOString();
      const hours = timeRange() === '1h' ? 1 : timeRange() === '6h' ? 6 : timeRange() === '24h' ? 24 : 168;
      const start = new Date(Date.now() - hours * 3600000).toISOString();
      const e = await api.get<ErrorMetrics[]>(`/monitoring/errors?start=${start}&end=${end}`);
      setErrors(e || []);
    } catch { /* ignore */ }
  };

  onMount(fetchAll);

  const onTimeRangeChange = (val: string) => {
    setTimeRange(val);
    fetchAll();
  };

  return (
    <div>
      <div class="mb-6 flex items-center justify-between">
        <div>
          <h2 class="text-2xl font-bold mb-2">监控</h2>
          <p class="text-secondary">查看网关运行指标</p>
        </div>
        <div class="flex items-center gap-2">
          <select class="input w-auto" value={timeRange()} onChange={(e) => onTimeRangeChange(e.currentTarget.value)}>
            <option value="1h">最近1小时</option>
            <option value="6h">最近6小时</option>
            <option value="24h">最近24小时</option>
            <option value="7d">最近7天</option>
          </select>
          <button class="btn btn-secondary" onClick={fetchAll}>刷新</button>
        </div>
      </div>

      {/* 概览指标卡片 */}
      <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
        <div class="card">
          <p class="text-secondary text-sm">总请求数</p>
          <h3 class="text-2xl font-bold">{metrics()?.totalRequests.toLocaleString() ?? '-'}</h3>
        </div>
        <div class="card">
          <p class="text-secondary text-sm">错误率</p>
          <h3 class="text-2xl font-bold text-danger">{metrics()?.errorRate.toFixed(1) ?? '-'}%</h3>
        </div>
        <div class="card">
          <p class="text-secondary text-sm">平均响应时间</p>
          <h3 class="text-2xl font-bold">{metrics()?.averageResponseTime.toFixed(0) ?? '-'}ms</h3>
        </div>
        <div class="card">
          <p class="text-secondary text-sm">错误请求数</p>
          <h3 class="text-2xl font-bold text-warning">{metrics()?.errorRequests.toLocaleString() ?? '-'}</h3>
        </div>
      </div>

      {/* 服务级别指标 */}
      <div class="card mb-6">
        <h3 class="font-semibold mb-4">服务指标</h3>
        <Show when={services().length > 0} fallback={<p class="text-secondary text-center py-4">暂无数据</p>}>
          <div class="overflow-x-auto">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b border-gray-200 dark:border-dark-200">
                  <th class="text-left py-2 px-3">路由</th>
                  <th class="text-right py-2 px-3">请求数</th>
                  <th class="text-right py-2 px-3">错误数</th>
                  <th class="text-right py-2 px-3">错误率</th>
                  <th class="text-right py-2 px-3">平均响应</th>
                </tr>
              </thead>
              <tbody>
                <For each={services()}>{(svc, _) => (
                  <tr class="border-b border-gray-100 dark:border-dark-200">
                    <td class="py-2 px-3 font-mono text-xs">{svc.serviceName}</td>
                    <td class="py-2 px-3 text-right">{svc.totalRequests}</td>
                    <td class="py-2 px-3 text-right text-danger">{svc.errorRequests}</td>
                    <td class="py-2 px-3 text-right">
                      <span class={svc.errorRate > 0 ? 'text-danger' : 'text-success'}>{svc.errorRate.toFixed(1)}%</span>
                    </td>
                    <td class="py-2 px-3 text-right">{svc.averageResponseTime.toFixed(1)}ms</td>
                  </tr>
                )}
                </For>
              </tbody>
            </table>
          </div>
        </Show>
      </div>

      {/* 错误分布 */}
      <div class="card">
        <h3 class="font-semibold mb-4">错误分布</h3>
        <Show when={errors().length > 0} fallback={<p class="text-secondary text-center py-4">暂无错误数据</p>}>
          <div class="space-y-3">
            <For each={errors()}>{(err) => (
              <div class="flex items-center justify-between p-3 bg-gray-50 dark:bg-dark-200 rounded-lg">
                <div class="flex items-center gap-3">
                  <span class={`px-2 py-1 rounded text-xs font-bold ${parseInt(err.errorCode) >= 500 ? 'bg-danger/10 text-danger' : 'bg-warning/10 text-warning'
                    }`}>{err.errorCode}</span>
                  <span class="text-sm">{err.errorMessage}</span>
                </div>
                <div class="flex items-center gap-4">
                  {err.routeId && <span class="text-xs text-secondary font-mono">{err.routeId}</span>}
                  <span class="text-sm font-bold">{err.count} 次</span>
                </div>
              </div>
            )}</For>
          </div>
        </Show>
      </div>
    </div>
  );
}
