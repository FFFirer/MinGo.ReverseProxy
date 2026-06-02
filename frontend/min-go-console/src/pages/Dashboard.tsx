import { createSignal, onMount } from 'solid-js';
import { api } from '../api/client';
import type { MetricsSummary, RequestMetrics } from '../types';

export default function Dashboard() {
  const [metrics, setMetrics] = createSignal<MetricsSummary | null>(null);
  const [requestMetrics, setRequestMetrics] = createSignal<RequestMetrics[]>([]);

  onMount(async () => {
    try {
      const data = await api.get<MetricsSummary>('/monitoring/metrics');
      setMetrics(data);
    } catch { /* ignore */ }

    try {
      const end = new Date().toISOString();
      const start = new Date(Date.now() - 3600000).toISOString();
      const data = await api.get<RequestMetrics[]>(`/monitoring/requests?start=${start}&end=${end}`);
      setRequestMetrics(data || []);
    } catch { /* ignore */ }
  });

  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">仪表盘</h2>
        <p class="text-secondary">实时监控网关运行状态</p>
      </div>

      {/* 统计卡片 */}
      <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-6">
        <div class="card">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-secondary text-sm">总请求数</p>
              <h3 class="text-2xl font-bold">{metrics()?.totalRequests.toLocaleString() || '0'}</h3>
              <p class="text-success text-sm mt-1"><i class="fa fa-arrow-up"></i> 实时</p>
            </div>
            <div class="w-12 h-12 rounded-full bg-primary/10 flex items-center justify-center text-primary">
              <i class="fa fa-refresh"></i>
            </div>
          </div>
        </div>
        <div class="card">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-secondary text-sm">错误率</p>
              <h3 class="text-2xl font-bold">{metrics()?.errorRate.toFixed(1) || '0'}%</h3>
            </div>
            <div class="w-12 h-12 rounded-full bg-danger/10 flex items-center justify-center text-danger">
              <i class="fa fa-exclamation-circle"></i>
            </div>
          </div>
        </div>
        <div class="card">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-secondary text-sm">平均响应时间</p>
              <h3 class="text-2xl font-bold">{metrics()?.averageResponseTime.toFixed(0) || '0'}ms</h3>
            </div>
            <div class="w-12 h-12 rounded-full bg-success/10 flex items-center justify-center text-success">
              <i class="fa fa-clock-o"></i>
            </div>
          </div>
        </div>
        <div class="card">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-secondary text-sm">活跃服务</p>
              <h3 class="text-2xl font-bold">{metrics()?.activeConnections || 0}</h3>
            </div>
            <div class="w-12 h-12 rounded-full bg-warning/10 flex items-center justify-center text-warning">
              <i class="fa fa-server"></i>
            </div>
          </div>
        </div>
      </div>

      {/* 图表 + 服务状态 */}
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-6">
        <div class="card">
          <h3 class="font-semibold mb-4">请求趋势</h3>
          <div class="h-64 bg-gray-50 dark:bg-dark-200 rounded-lg flex items-center justify-center">
            {requestMetrics().length > 0 ? (
              <div class="w-full h-full p-4">
                {/* Chart.js 集成在这里 */}
                <p class="text-secondary text-center">请求趋势图表 (Chart.js)</p>
                <div class="mt-4 space-y-2">
                  {requestMetrics().slice(-10).map((rm) => (
                    <div class="flex items-center justify-between text-sm">
                      <span>{new Date(rm.timestamp).toLocaleTimeString()}</span>
                      <span>{rm.count} 请求</span>
                    </div>
                  ))}
                </div>
              </div>
            ) : (
              <p class="text-secondary">暂无数据</p>
            )}
          </div>
        </div>
        <div class="card">
          <h3 class="font-semibold mb-4">服务状态</h3>
          <div class="space-y-4">
            <div class="flex items-center justify-between">
              <div class="flex items-center space-x-3">
                <div class="w-3 h-3 rounded-full bg-success"></div>
                <span>API 网关</span>
              </div>
              <div class="w-1/2 bg-gray-200 dark:bg-dark-200 rounded-full h-2">
                <div class="bg-success h-2 rounded-full" style="width: 100%"></div>
              </div>
              <span class="text-sm">100%</span>
            </div>
            <div class="flex items-center justify-between">
              <div class="flex items-center space-x-3">
                <div class="w-3 h-3 rounded-full bg-success"></div>
                <span>后端服务</span>
              </div>
              <div class="w-1/2 bg-gray-200 dark:bg-dark-200 rounded-full h-2">
                <div class="bg-success h-2 rounded-full" style="width: 95%"></div>
              </div>
              <span class="text-sm">95%</span>
            </div>
          </div>
        </div>
      </div>

      {/* 最近请求 */}
      <div class="card">
        <div class="flex items-center justify-between mb-4">
          <h3 class="font-semibold">网关概览</h3>
        </div>
        <div class="grid grid-cols-2 md:grid-cols-4 gap-4">
          <div class="p-4 bg-gray-50 dark:bg-dark-200 rounded-lg text-center">
            <p class="text-2xl font-bold text-primary">{metrics()?.totalRequests || 0}</p>
            <p class="text-sm text-secondary">总请求</p>
          </div>
          <div class="p-4 bg-gray-50 dark:bg-dark-200 rounded-lg text-center">
            <p class="text-2xl font-bold text-danger">{metrics()?.errorRequests || 0}</p>
            <p class="text-sm text-secondary">错误请求</p>
          </div>
          <div class="p-4 bg-gray-50 dark:bg-dark-200 rounded-lg text-center">
            <p class="text-2xl font-bold text-success">{metrics()?.averageResponseTime.toFixed(0) || 0}ms</p>
            <p class="text-sm text-secondary">平均响应</p>
          </div>
          <div class="p-4 bg-gray-50 dark:bg-dark-200 rounded-lg text-center">
            <p class="text-2xl font-bold text-warning">{metrics()?.cpuUsage.toFixed(0) || 0}%</p>
            <p class="text-sm text-secondary">CPU使用</p>
          </div>
        </div>
      </div>
    </div>
  );
}
