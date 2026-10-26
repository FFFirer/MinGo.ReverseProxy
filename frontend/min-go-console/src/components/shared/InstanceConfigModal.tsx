import { createSignal, onMount, Show, For } from 'solid-js';
import { api } from '../../api/client';
import type {
  GatewayInstance,
  InstanceConfigResponse,
  InstanceRouteInfo,
  InstanceClusterInfo,
} from '../../types';
import type { Column } from './Table';
import Table from './Table';

interface Props {
  instance: GatewayInstance;
  onClose: () => void;
}

const policyLabels: Record<string, string> = {
  RoundRobin: '轮询',
  LeastRequests: '最少连接',
  Random: '随机',
  PowerOfTwoChoices: '幂次选择',
  FirstAlphabetical: '字母序优先',
};

function policyLabel(policy: string): string {
  return policyLabels[policy] || policy;
}

export default function InstanceConfigModal(props: Props) {
  const [config, setConfig] = createSignal<InstanceConfigResponse | null>(null);
  const [loading, setLoading] = createSignal(true);
  const [error, setError] = createSignal<string | null>(null);

  const fetchConfig = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.get<InstanceConfigResponse>(
        `/instances/${props.instance.instanceId}/config`
      );
      setConfig(data);
    } catch (err: any) {
      setError(err.message || '获取配置失败');
    } finally {
      setLoading(false);
    }
  };

  // 首次打开时请求
  onMount(fetchConfig);

  const handleOverlayClick = (e: MouseEvent) => {
    if (e.target === e.currentTarget) props.onClose();
  };

  const routeColumns: Column<InstanceRouteInfo>[] = [
    {
      key: 'routeId',
      title: '路由ID',
      mobileVisible: true,
      cell: (route) => <span class="font-mono text-xs">{route.routeId}</span>,
    },
    {
      key: 'clusterId',
      title: '目标集群',
      cell: (route) => route.clusterId,
    },
    {
      key: 'path',
      title: '匹配路径',
      mobileVisible: true,
      cell: (route) => <span class="font-mono text-xs">{route.match?.path || '-'}</span>,
    },
    {
      key: 'hosts',
      title: '匹配域名',
      cell: (route) => <span class="font-mono text-xs">{route.match?.hosts?.join(', ') || '-'}</span>,
    },
  ];

  return (
    <div
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      onClick={handleOverlayClick}
    >
      <div class="bg-white dark:bg-dark-100 rounded-xl shadow-2xl w-full max-w-3xl max-h-[85vh] flex flex-col mx-4">
        {/* Header */}
        <div class="flex items-center justify-between px-6 py-4 border-b border-gray-200 dark:border-dark-200">
          <div>
            <h3 class="text-lg font-bold">实例配置 - {props.instance.name}</h3>
            <p class="text-xs text-secondary mt-0.5">
              {props.instance.listenerAddresses?.[0] || props.instance.address || '-'}
            </p>
          </div>
          <button
            onClick={props.onClose}
            class="text-secondary hover:text-gray-800 dark:hover:text-gray-200 text-xl leading-none"
            title="关闭"
          >
            ✕
          </button>
        </div>

        {/* Content */}
        <div class="flex-1 overflow-y-auto px-6 py-4">
          <Show when={loading()}>
            <div class="flex items-center justify-center py-12">
              <div class="text-secondary text-sm">加载中...</div>
            </div>
          </Show>

          <Show when={error()}>
            <div class="bg-danger/10 border border-danger/30 text-danger rounded-lg px-4 py-3 text-sm mb-4">
              {error()}
            </div>
          </Show>

          <Show when={!loading() && config() !== null}>
            <Show when={!error()} fallback={
              <div class="bg-danger/10 border border-danger/30 text-danger rounded-lg px-4 py-3 text-sm mb-4">
                {error()}
              </div>
            }>
              <Show when={config()!} fallback={<div class="text-secondary text-sm py-8 text-center">无配置数据</div>}>
                {/* Version */}
                <div class="text-xs text-secondary mb-4">
                  配置版本: {config()?.version ?? '-'}
                  <span class="mx-2">|</span>
                  更新于: {config()?.changeTime ? new Date(config()!.changeTime).toLocaleString() : '-'}
                </div>

                {/* Routes Panel */}
                <div class="mb-6">
                  <h4 class="text-sm font-semibold mb-2 flex items-center gap-2">
                    <span>路由规则 (Routes)</span>
                    <span class="badge badge-success text-xs">{config()?.routes?.length ?? 0}</span>
                  </h4>
                  <Show
                    when={(config()?.routes?.length ?? 0) > 0}
                    fallback={
                      <div class="text-secondary text-sm py-4 text-center bg-gray-50 dark:bg-dark-200 rounded-lg">
                        暂无路由规则
                      </div>
                    }
                  >
                    <Table
                      columns={routeColumns}
                      data={config()?.routes ?? []}
                      rowKey={(r) => r.routeId}
                      compact
                    />
                  </Show>
                </div>

                {/* Clusters Panel */}
                <div>
                  <h4 class="text-sm font-semibold mb-2 flex items-center gap-2">
                    <span>目标集群 (Clusters)</span>
                    <span class="badge badge-success text-xs">{config()?.clusters?.length ?? 0}</span>
                  </h4>
                  <Show
                    when={(config()?.clusters?.length ?? 0) > 0}
                    fallback={
                      <div class="text-secondary text-sm py-4 text-center bg-gray-50 dark:bg-dark-200 rounded-lg">
                        暂无目标集群
                      </div>
                    }
                  >
                    <div class="grid gap-3">
                      <For each={config()?.clusters ?? []}>{(cluster: InstanceClusterInfo) => (
                        <div class="border border-gray-200 dark:border-dark-200 rounded-lg p-4">
                          <div class="flex items-center justify-between mb-2">
                            <span class="font-semibold text-sm">{cluster.clusterId}</span>
                            <span class="badge badge-success text-xs">
                              {policyLabel(cluster.loadBalancingPolicy)}
                            </span>
                          </div>
                          <div class="space-y-1">
                            <For each={cluster.destinations}>{(dest) => (
                              <div class="flex items-center gap-2 text-xs">
                                <span
                                  class={`w-2 h-2 rounded-full ${
                                    dest.healthy ? 'bg-success' : 'bg-danger'
                                  }`}
                                />
                                <span class="font-mono">{dest.address}</span>
                                <span class={dest.healthy ? 'text-success' : 'text-danger'}>
                                  {dest.healthy ? '健康' : '不健康'}
                                </span>
                              </div>
                            )}</For>
                          </div>
                        </div>
                      )}</For>
                    </div>
                  </Show>
                </div>
              </Show>
            </Show>
          </Show>
        </div>

        {/* Footer */}
        <div class="flex items-center justify-end gap-2 px-6 py-3 border-t border-gray-200 dark:border-dark-200">
          <button
            onClick={fetchConfig}
            disabled={loading()}
            class="btn-text-primary text-sm"
            title="刷新"
          >
            刷新
          </button>
          <button onClick={props.onClose} class="btn-text-primary text-sm">
            关闭
          </button>
        </div>
      </div>
    </div>
  );
}
