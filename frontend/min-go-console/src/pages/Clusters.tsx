import { createSignal, onMount } from 'solid-js';
import { api } from '../api/client';
import type { ClusterConfig } from '../types';

export default function ClustersPage() {
  const [clusters, setClusters] = createSignal<ClusterConfig[]>([]);

  onMount(async () => {
    try {
      setClusters(await api.get<ClusterConfig[]>('/apimanagement/clusters'));
    } catch { /* ignore */ }
  });

  const handleDelete = async (id: string) => {
    if (!confirm('确定删除此集群？')) return;
    await api.delete(`/apimanagement/clusters/${id}`);
    setClusters(await api.get<ClusterConfig[]>('/apimanagement/clusters'));
  };

  return (
    <div>
      <div class="mb-6 flex items-center justify-between">
        <div>
          <h2 class="text-2xl font-bold mb-2">集群管理</h2>
          <p class="text-secondary">配置和管理后端服务集群</p>
        </div>
        <button class="btn btn-primary">
          <i class="fa fa-plus mr-2"></i>添加集群
        </button>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {clusters().map((cluster) => (
          <div class="card">
            <div class="flex items-center justify-between mb-4">
              <h3 class="font-semibold">{cluster.name || cluster.id}</h3>
              <span class="badge badge-success">健康</span>
            </div>
            <div class="space-y-3">
              {cluster.destinations && Object.entries(cluster.destinations).map(([id, dest]) => (
                <div class="flex items-center justify-between p-3 bg-gray-50 dark:bg-dark-200 rounded-lg">
                  <div>
                    <p class="font-medium">{id}</p>
                    <p class="text-sm text-secondary">{dest.address}</p>
                  </div>
                  <span class={`badge ${dest.healthy ? 'badge-success' : 'badge-warning'}`}>
                    {dest.healthy ? '在线' : '离线'}
                  </span>
                </div>
              ))}
            </div>
            <div class="mt-4 flex justify-between items-center">
              <span class="text-sm text-secondary">策略: {cluster.loadBalancingPolicy}</span>
              <div class="flex space-x-2">
                <button class="btn btn-secondary text-sm" onClick={() => handleDelete(cluster.id)}>
                  <i class="fa fa-trash mr-1"></i>删除
                </button>
              </div>
            </div>
          </div>
        ))}
        {clusters().length === 0 && (
          <div class="col-span-2 text-center py-12 text-secondary">暂无集群</div>
        )}
      </div>
    </div>
  );
}
