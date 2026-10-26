import { createSignal, onMount, Show } from 'solid-js';
import { api } from '../api/client';
import { addToast } from '../store/toast';
import type { GatewayInstance } from '../types';
import type { Column } from '../components/shared/Table';
import Table from '../components/shared/Table';
import InstanceConfigModal from '../components/shared/InstanceConfigModal';

export default function InstancesPage() {
  const [instances, setInstances] = createSignal<GatewayInstance[]>([]);
  const [configTarget, setConfigTarget] = createSignal<GatewayInstance | null>(null);

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
      await fetchInstances();
    } catch (err) {
      addToast('error', `删除实例失败: ${(err as Error).message}`);
    }
  };

  const onlineCount = () => instances().filter(i => i.status === 'Online').length;
  const offlineCount = () => instances().filter(i => i.status === 'Offline').length;
  const timeoutCount = () => instances().filter(i => i.status === 'HeartbeatTimeout').length;

  const columns: Column<GatewayInstance>[] = [
    {
      key: 'name',
      title: '实例名称',
      mobileVisible: true,
      cell: (inst) => (
        <div>
          <div class="font-medium">{inst.name}</div>
          {inst.version && inst.version !== 'unknown' && (
            <div class="text-xs text-secondary">{inst.version}</div>
          )}
        </div>
      ),
    },
    {
      key: 'address',
      title: '地址',
      cell: (inst) => <span class="text-sm">{inst.address || inst.listenerAddresses?.[0] || '-'}</span>,
    },
    {
      key: 'status',
      title: '状态',
      mobileVisible: true,
      cell: (inst) => (
        <span class={`badge ${statusBadge(inst.status)}`}>{statusLabel(inst.status)}</span>
      ),
    },
    {
      key: 'cpu',
      title: 'CPU',
      cell: (inst) => <span class="text-sm">{formatMetric(inst.cpuUsage)}%</span>,
    },
    {
      key: 'memory',
      title: '内存',
      cell: (inst) => <span class="text-sm">{formatMetric(inst.memoryUsage)}%</span>,
    },
    {
      key: 'requests',
      title: '请求数',
      cell: (inst) => (
        <span class="text-sm">
          <span class="text-success">{formatCount(inst.totalRequests)}</span>
          {inst.errorRequests > 0 && (
            <span class="text-danger ml-1">({formatCount(inst.errorRequests)})</span>
          )}
        </span>
      ),
    },
    {
      key: 'errorRate',
      title: '错误率',
      cell: (inst) => (
        <span class={`text-sm ${inst.errorRate > 5 ? 'text-danger' : inst.errorRate > 1 ? 'text-warning' : ''}`}>
          {formatMetric(inst.errorRate, 1)}%
        </span>
      ),
    },
    {
      key: 'uptime',
      title: '运行时长',
      cell: (inst) => <span class="text-sm">{formatUptime(inst.uptime)}</span>,
    },
    {
      key: 'lastHeartbeat',
      title: '最后心跳',
      cell: (inst) => (
        <span class="text-sm">
          {inst.lastHeartbeat ? new Date(inst.lastHeartbeat).toLocaleString() : '-'}
        </span>
      ),
    },
    {
      key: 'actions',
      title: '操作',
      mobileVisible: true,
      class: 'w-20',
      cell: (inst) => (
        <div class="flex gap-2">
          <button
            onClick={(e) => { e.stopPropagation(); setConfigTarget(inst); }}
            class="btn-text-primary text-sm"
            title="查看实例配置"
          >
            查看
          </button>
          <button
            onClick={(e) => { e.stopPropagation(); handleDelete(inst); }}
            class="text-danger hover:text-red-400 text-sm"
            title="删除实例"
          >
            删除
          </button>
        </div>
      ),
    },
  ];

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
        <Table
          columns={columns}
          data={instances()}
          rowKey={(i) => i.instanceId}
          emptyMessage={
            <div>
              <div class="text-lg mb-1">暂无实例</div>
              <div class="text-sm">等待数据面通过 gRPC 连接后，实例将自动出现在此列表</div>
            </div>
          }
        />
      </div>

      <Show when={configTarget()}>
        {(target) => (
          <InstanceConfigModal
            instance={target()}
            onClose={() => setConfigTarget(null)}
          />
        )}
      </Show>
    </div>
  );
}
