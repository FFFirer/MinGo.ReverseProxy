import { createSignal, onMount, For } from 'solid-js';
import { api } from '../api/client';
import type { GatewayInstance } from '../types';

export default function InstancesPage() {
  const [instances, setInstances] = createSignal<GatewayInstance[]>([]);

  const fetchInstances = async () => {
    try {
      const data = await api.get<any>('/instances');
      setInstances(data?.instances || []);
    } catch { /* ignore */ }
  };

  onMount(fetchInstances);

  const statusBadge = (status: string) => {
    switch (status) {
      case 'Online': return 'badge-success';
      case 'Starting': return 'badge-warning';
      case 'HeartbeatTimeout': return 'badge-warning';
      case 'Unhealthy': return 'badge-danger';
      default: return 'badge-danger';
    }
  };

  const statusLabel = (status: string) => {
    switch (status) {
      case 'Online': return '在线';
      case 'Offline': return '离线';
      case 'HeartbeatTimeout': return '心跳超时';
      case 'Unhealthy': return '不健康';
      case 'Starting': return '启动中';
      default: return '未知';
    }
  };

  const formatUptime = (uptime: string) => {
    // ISO 8601 duration format from .NET TimeSpan
    if (!uptime) return '-';
    const match = uptime.match(/(\d+)\.(\d{2}):(\d{2}):(\d{2})/);
    if (match) {
      const [, days, hours, mins, secs] = match;
      const parts: string[] = [];
      if (+days > 0) parts.push(`${days}d`);
      if (+hours > 0) parts.push(`${hours}h`);
      parts.push(`${mins}m`);
      parts.push(`${secs}s`);
      return parts.join(' ');
    }
    // Try parsing as a shorter format
    const short = uptime.match(/(\d{2}):(\d{2}):(\d{2})/);
    if (short) {
      const [, h, m, s] = short;
      return `${+h}h ${+m}m ${+s}s`;
    }
    return uptime;
  };

  const formatMetric = (val: number, digits = 1) =>
    val != null ? val.toFixed(digits) : '-';

  const formatCount = (val: number) =>
    val != null ? val.toLocaleString() : '-';

  const handleDelete = async (inst: GatewayInstance) => {
    if (!confirm(`确定要删除实例 "${inst.name}" (${inst.instanceId}) 吗？`)) return;
    try {
      await api.delete(`/instances/${inst.instanceId}`);
      setInstances(prev => prev.filter(i => i.instanceId !== inst.instanceId));
    } catch (err) {
      console.error('Failed to delete instance:', err);
    }
  };

  const onlineCount = () => instances().filter(i => i.status === 'Online').length;
  const offlineCount = () => instances().filter(i => i.status === 'Offline').length;
  const timeoutCount = () => instances().filter(i => i.status === 'HeartbeatTimeout').length;

  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">实例管理</h2>
        <p class="text-secondary">管理已连接的网关实例，实时查看运行状态和指标</p>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-4 gap-4 mb-6">
        <div class="card text-center">
          <p class="text-secondary text-sm mb-1">总实例</p>
          <h4 class="text-2xl font-bold">{instances().length}</h4>
        </div>
        <div class="card text-center">
          <p class="text-secondary text-sm mb-1">在线</p>
          <h4 class="text-2xl font-bold text-success">{onlineCount()}</h4>
        </div>
        <div class="card text-center">
          <p class="text-secondary text-sm mb-1">心跳超时</p>
          <h4 class="text-2xl font-bold text-warning">{timeoutCount()}</h4>
        </div>
        <div class="card text-center">
          <p class="text-secondary text-sm mb-1">离线</p>
          <h4 class="text-2xl font-bold text-danger">{offlineCount()}</h4>
        </div>
      </div>

      <div class="card">
        <div class="overflow-x-auto">
          <table class="w-full">
            <thead>
              <tr class="border-b border-gray-200 dark:border-dark-200">
                <th class="text-left py-3 px-4 font-medium text-secondary">实例名称</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">地址</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">状态</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">CPU</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">内存</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">请求数</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">错误率</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">运行时长</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">最后心跳</th>
                <th class="text-left py-3 px-4 font-medium text-secondary w-20">操作</th>
              </tr>
            </thead>
            <tbody>
              <For each={instances()}>{(inst) => (
                <tr class="border-b border-gray-100 dark:border-dark-100 hover:bg-gray-50 dark:hover:bg-dark-100/50">
                  <td class="py-3 px-4">
                    <div class="font-medium">{inst.name}</div>
                    {inst.version && inst.version !== 'unknown' && (
                      <div class="text-xs text-secondary">{inst.version}</div>
                    )}
                  </td>
                  <td class="py-3 px-4 text-sm">{inst.address || (inst.listenerAddresses?.[0]) || '-'}</td>
                  <td class="py-3 px-4">
                    <span class={`badge ${statusBadge(inst.status)}`}>{statusLabel(inst.status)}</span>
                  </td>
                  <td class="py-3 px-4 text-sm">{formatMetric(inst.cpuUsage)}%</td>
                  <td class="py-3 px-4 text-sm">{formatMetric(inst.memoryUsage)}%</td>
                  <td class="py-3 px-4 text-sm">
                    <span class="text-success">{formatCount(inst.totalRequests)}</span>
                    {inst.errorRequests > 0 && (
                      <span class="text-danger ml-1">({formatCount(inst.errorRequests)})</span>
                    )}
                  </td>
                  <td class="py-3 px-4 text-sm">
                    <span class={inst.errorRate > 5 ? 'text-danger' : inst.errorRate > 1 ? 'text-warning' : ''}>
                      {formatMetric(inst.errorRate, 1)}%
                    </span>
                  </td>
                  <td class="py-3 px-4 text-sm">{formatUptime(inst.uptime)}</td>
                  <td class="py-3 px-4 text-sm">
                    {inst.lastHeartbeat ? new Date(inst.lastHeartbeat).toLocaleString() : '-'}
                  </td>
                  <td class="py-3 px-4">
                    <button
                      onClick={() => handleDelete(inst)}
                      class="text-danger hover:text-red-400 text-sm"
                      title="删除实例"
                    >删除</button>
                  </td>
                </tr>
              )}</For>
              {instances().length === 0 && (
                <tr><td colspan="10" class="py-12 text-center text-secondary">
                  <div class="text-lg mb-1">暂无实例</div>
                  <div class="text-sm">等待数据面通过 gRPC 连接后，实例将自动出现在此列表</div>
                </td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
