import { createSignal, onMount, onCleanup, For, Show } from 'solid-js';
import { api } from '../api/client';
import { addToast } from '../store/toast';
import type { MetricsSummary, RequestMetrics, GatewayInstance, ServiceMetrics, AccessLog } from '../types';
import { FaSolidArrowUp, FaSolidExclamationCircle, FaSolidClock, FaSolidServer, FaSolidRefresh } from 'solid-icons/fa';

export default function Dashboard() {
  const [metrics, setMetrics] = createSignal<MetricsSummary | null>(null);
  const [requestMetrics, setRequestMetrics] = createSignal<RequestMetrics[]>([]);
  const [instances, setInstances] = createSignal<GatewayInstance[]>([]);
  const [services, setServices] = createSignal<ServiceMetrics[]>([]);
  const [recentLogs, setRecentLogs] = createSignal<AccessLog[]>([]);

  const fetchAll = async () => {
    try {
      const m = await api.get<MetricsSummary>('/monitoring/metrics');
      setMetrics(m);
    } catch { /* silent */ }

    try {
      const end = new Date().toISOString();
      const start = new Date(Date.now() - 3600000).toISOString();
      const data = await api.get<RequestMetrics[]>(`/monitoring/requests?start=${start}&end=${end}`);
      setRequestMetrics(data || []);
    } catch { /* silent */ }

    try {
      const data = await api.get<any>('/instances');
      setInstances(data?.instances || []);
    } catch { /* silent */ }

    try {
      const s = await api.get<ServiceMetrics[]>('/monitoring/services');
      setServices(s || []);
    } catch { /* silent */ }

    try {
      const logs = await api.get<AccessLog[]>('/logs/access?pageSize=8');
      setRecentLogs(logs || []);
    } catch { /* silent */ }
  };

  let timer: ReturnType<typeof setInterval>;
  onMount(() => {
    fetchAll();
    timer = setInterval(fetchAll, 15000);
  });
  onCleanup(() => clearInterval(timer));

  const onlineInstances = () => instances().filter(i => i.status === 'Online').length;
  const totalInstances = () => instances().length;

  const maxRequests = () => Math.max(...requestMetrics().map(r => r.count), 1);

  const statusColor = (status: number) => {
    if (status >= 500) return 'text-danger';
    if (status >= 400) return 'text-warning';
    return 'text-gray-600 dark:text-gray-400';
  };

  return (
    <div>
      <div class="mb-6 flex items-center justify-between">
        <div>
          <h2 class="text-2xl font-bold mb-1">仪表盘</h2>
          <p class="text-secondary text-sm">实时监控网关运行状态</p>
        </div>
        <button class="btn btn-secondary text-sm" onClick={fetchAll}>
          <FaSolidRefresh class="mr-1" /> 刷新
        </button>
      </div>

      {/* 统计卡片 */}
      <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
        <div class="card">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-secondary text-sm">总请求数</p>
              <h3 class="text-2xl font-bold">{metrics()?.totalRequests.toLocaleString() ?? '-'}</h3>
            </div>
            <div class="w-10 h-10 rounded-full bg-primary/10 flex items-center justify-center text-primary">
              <FaSolidArrowUp />
            </div>
          </div>
        </div>
        <div class="card">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-secondary text-sm">错误率</p>
              <h3 class={`text-2xl font-bold ${(metrics()?.errorRate ?? 0) > 5 ? 'text-danger' : ''}`}>
                {metrics()?.errorRate.toFixed(1) ?? '-'}%
              </h3>
            </div>
            <div class="w-10 h-10 rounded-full bg-danger/10 flex items-center justify-center text-danger">
              <FaSolidExclamationCircle />
            </div>
          </div>
        </div>
        <div class="card">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-secondary text-sm">平均响应</p>
              <h3 class="text-2xl font-bold">{metrics()?.averageResponseTime.toFixed(0) ?? '-'}ms</h3>
            </div>
            <div class="w-10 h-10 rounded-full bg-success/10 flex items-center justify-center text-success">
              <FaSolidClock />
            </div>
          </div>
        </div>
        <div class="card">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-secondary text-sm">实例</p>
              <h3 class="text-2xl font-bold">
                <span class="text-success">{onlineInstances()}</span>
                <span class="text-secondary text-base">/{totalInstances()}</span>
              </h3>
            </div>
            <div class="w-10 h-10 rounded-full bg-warning/10 flex items-center justify-center text-warning">
              <FaSolidServer />
            </div>
          </div>
        </div>
      </div>

      {/* 请求趋势 + 实例状态 */}
      <div class="grid grid-cols-1 lg:grid-cols-3 gap-4 mb-6">
        {/* 请求趋势 */}
        <div class="card lg:col-span-2">
          <h3 class="font-semibold mb-4">请求趋势 <span class="text-xs text-secondary font-normal">（最近1小时）</span></h3>
          <Show when={requestMetrics().length > 0} fallback={<p class="text-secondary text-center py-12">暂无数据</p>}>
            <div class="flex items-end gap-1 h-40">
              <For each={requestMetrics()}>{(rm) => (
                <div class="flex-1 flex flex-col items-center justify-end h-full group relative">
                  <div
                    class="w-full rounded-t bg-primary/70 hover:bg-primary transition-colors min-h-[2px]"
                    style={{ height: `${(rm.count / maxRequests()) * 100}%` }}
                  />
                  <div class="absolute bottom-full mb-1 hidden group-hover:block bg-gray-800 text-white text-xs px-2 py-1 rounded whitespace-nowrap z-10">
                    {rm.count} 请求 / {rm.averageResponseTime.toFixed(0)}ms
                  </div>
                </div>
              )}</For>
            </div>
            <div class="flex justify-between text-xs text-secondary mt-1">
              <span>{requestMetrics().length > 0 ? new Date(requestMetrics()[0].timestamp).toLocaleTimeString() : ''}</span>
              <span>{requestMetrics().length > 0 ? new Date(requestMetrics()[requestMetrics().length - 1].timestamp).toLocaleTimeString() : ''}</span>
            </div>
          </Show>
        </div>

        {/* 实例状态 */}
        <div class="card">
          <h3 class="font-semibold mb-4">实例状态</h3>
          <Show when={instances().length > 0} fallback={<p class="text-secondary text-center py-8">暂无实例</p>}>
            <div class="space-y-3">
              <For each={instances()}>{(inst) => (
                <div class="flex items-center justify-between p-2 rounded-lg bg-gray-50 dark:bg-dark-200">
                  <div class="flex items-center gap-2">
                    <div class={`w-2 h-2 rounded-full ${inst.isHealthy ? 'bg-success' : 'bg-danger'}`} />
                    <div>
                      <p class="text-sm font-medium">{inst.name}</p>
                      <p class="text-xs text-secondary">{inst.totalRequests} 请求</p>
                    </div>
                  </div>
                  <span class={`text-xs ${inst.isHealthy ? 'text-success' : 'text-danger'}`}>
                    {inst.isHealthy ? '在线' : '离线'}
                  </span>
                </div>
              )}</For>
            </div>
          </Show>
        </div>
      </div>

      {/* 服务指标 + 最近日志 */}
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* 服务级别指标 */}
        <div class="card">
          <h3 class="font-semibold mb-4">路由指标</h3>
          <Show when={services().length > 0} fallback={<p class="text-secondary text-center py-8">暂无数据</p>}>
            <div class="space-y-2">
              <For each={services().slice(0, 8)}>{(svc) => (
                <div class="flex items-center justify-between text-sm">
                  <span class="font-mono text-xs truncate max-w-[160px]" title={svc.serviceName}>{svc.serviceName}</span>
                  <div class="flex items-center gap-3">
                    <span>{svc.totalRequests} 请求</span>
                    <span class={svc.errorRate > 0 ? 'text-danger' : 'text-success'}>{svc.errorRate.toFixed(0)}%</span>
                    <span class="text-secondary">{svc.averageResponseTime.toFixed(0)}ms</span>
                  </div>
                </div>
              )}</For>
            </div>
          </Show>
        </div>

        {/* 最近日志 */}
        <div class="card">
          <div class="flex items-center justify-between mb-4">
            <h3 class="font-semibold">最近请求</h3>
            <a href="/logs" class="text-xs text-primary hover:underline">查看全部</a>
          </div>
          <Show when={recentLogs().length > 0} fallback={<p class="text-secondary text-center py-8">暂无日志</p>}>
            <div class="space-y-1 font-mono text-xs">
              <For each={recentLogs()}>{(log) => (
                <div class={`flex items-center gap-2 ${statusColor(log.statusCode)}`}>
                  <span class="text-secondary shrink-0">{new Date(log.timestamp).toLocaleTimeString()}</span>
                  <span class={`shrink-0 font-bold ${log.statusCode >= 400 ? 'text-danger' : ''}`}>{log.statusCode}</span>
                  <span class="shrink-0">{log.method}</span>
                  <span class="truncate">{log.path}</span>
                  <span class="text-secondary shrink-0 ml-auto">{log.durationMs}ms</span>
                </div>
              )}</For>
            </div>
          </Show>
        </div>
      </div>
    </div>
  );
}
