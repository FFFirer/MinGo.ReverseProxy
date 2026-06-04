import { createSignal, onMount, For } from 'solid-js';
import { api } from '../api/client';
import type { GatewayInstance } from '../types';

export default function InstancesPage() {
  const [instances, setInstances] = createSignal<GatewayInstance[]>([]);

  onMount(async () => {
    try {
      const data = await api.get<any>('/instances');
      setInstances(data?.instances || []);
    } catch { /* ignore */ }
  });

  const statusBadge = (status: string) => {
    switch (status) {
      case 'Online': return 'badge-success';
      case 'Starting': return 'badge-warning';
      default: return 'badge-danger';
    }
  };

  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">实例管理</h2>
        <p class="text-secondary">管理网关实例的在线状态</p>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-3 gap-6 mb-6">
        <div class="card text-center">
          <p class="text-secondary text-sm mb-1">总实例数</p>
          <h4 class="text-2xl font-bold">{instances().length}</h4>
        </div>
        <div class="card text-center">
          <p class="text-secondary text-sm mb-1">在线实例</p>
          <h4 class="text-2xl font-bold text-success">{instances().filter(i => i.status === 'Online').length}</h4>
        </div>
        <div class="card text-center">
          <p class="text-secondary text-sm mb-1">离线实例</p>
          <h4 class="text-2xl font-bold text-danger">{instances().filter(i => i.status !== 'Online').length}</h4>
        </div>
      </div>

      <div class="card">
        <div class="overflow-x-auto">
          <table class="w-full">
            <thead>
              <tr class="border-b border-gray-200 dark:border-dark-200">
                <th class="text-left py-3 px-4 font-medium text-secondary">实例名称</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">IP地址</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">状态</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">最后心跳</th>
              </tr>
            </thead>
            <tbody>
              <For each={instances()}>{(inst) => (
                <tr class="border-b border-gray-100 dark:border-dark-100 hover:bg-gray-50 dark:hover:bg-dark-100/50">
                  <td class="py-3 px-4 font-medium">{inst.name}</td>
                  <td class="py-3 px-4">{inst.ipAddress}</td>
                  <td class="py-3 px-4"><span class={`badge ${statusBadge(inst.status)}`}>{inst.status}</span></td>
                  <td class="py-3 px-4">{inst.lastHeartbeat ? new Date(inst.lastHeartbeat).toLocaleString() : '-'}</td>
                </tr>
              )}</For>
              {instances().length === 0 && (
                <tr><td colspan="4" class="py-8 text-center text-secondary">暂无实例</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
