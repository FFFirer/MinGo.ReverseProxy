import { For, Show, createMemo, createSignal, onMount } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import { api } from '../api/client';
import { addToast } from '../store/toast';
import type {
  TopologyEdge,
  TopologyNode,
  TopologyNodeStatus,
  TopologyResponse,
  TopologySeverity,
} from '../types';
import TopologyGraph from '../components/topology/TopologyGraph';
import {
  LAYER_LINKS,
  LAYER_TITLES,
  adjacency,
  closure,
} from '../components/topology/graphLayout';
import {
  FaSolidSearch,
  FaSolidSpinner,
  FaSolidSync,
  FaSolidTriangleExclamation,
  FaSolidXmark,
} from 'solid-icons/fa';

/** 同样为了 Tailwind v4 的字面量扫描，徽标样式固定查表 */
const STATUS_BADGE: Record<TopologyNodeStatus, string> = {
  ok: 'badge badge-success',
  warning: 'badge badge-warning',
  danger: 'badge badge-danger',
  disabled: 'badge bg-gray-100 text-gray-600 dark:bg-dark-200 dark:text-gray-300',
};

const STATUS_TEXT: Record<TopologyNodeStatus, string> = {
  ok: '链路完整',
  warning: '需关注',
  danger: '会失败',
  disabled: '已停用',
};

const SEVERITY_META: Record<TopologySeverity, { label: string; badge: string }> = {
  danger: { label: '阻断', badge: 'badge badge-danger' },
  warning: { label: '关注', badge: 'badge badge-warning' },
  info: { label: '提示', badge: 'badge bg-gray-100 text-gray-600 dark:bg-dark-200 dark:text-gray-300' },
};

const SEVERITY_ORDER: TopologySeverity[] = ['danger', 'warning', 'info'];

export default function TopologyPage() {
  const navigate = useNavigate();
  const [data, setData] = createSignal<TopologyResponse | null>(null);
  const [loading, setLoading] = createSignal(true);
  const [error, setError] = createSignal<string | null>(null);
  const [search, setSearch] = createSignal('');
  const [onlyIssues, setOnlyIssues] = createSignal(false);
  const [selectedId, setSelectedId] = createSignal<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      setData(await api.getTopology());
    } catch (err) {
      setError((err as Error).message);
      addToast('error', `加载拓扑失败: ${(err as Error).message}`);
    } finally {
      setLoading(false);
    }
  };

  onMount(load);

  const matches = (node: TopologyNode) => {
    const query = search().trim().toLowerCase();
    if (!query) return true;
    return (
      node.label.toLowerCase().includes(query) ||
      (node.subLabel ?? '').toLowerCase().includes(query) ||
      node.details.some((d) => (d.value ?? '').toLowerCase().includes(query))
    );
  };

  /** 搜索按节点过滤；"只看异常"保留异常节点及其上下游整条链路，避免只看到一个孤点。 */
  const visible = createMemo<{ nodes: TopologyNode[]; edges: TopologyEdge[] }>(() => {
    const current = data();
    if (!current) return { nodes: [], edges: [] };

    let nodes = current.nodes.filter(matches);
    if (onlyIssues()) {
      const seeds = nodes
        .filter((n) => n.status === 'warning' || n.status === 'danger')
        .map((n) => n.id);
      if (!seeds.length) return { nodes: [], edges: [] };
      const keep = closure(seeds, adjacency(current.edges));
      nodes = nodes.filter((n) => keep.has(n.id));
    }

    const ids = new Set(nodes.map((n) => n.id));
    return {
      nodes,
      edges: current.edges.filter((e) => ids.has(e.source) && ids.has(e.target)),
    };
  });

  const selected = createMemo(() => {
    const current = data();
    const id = selectedId();
    if (!current || !id) return null;
    return current.nodes.find((n) => n.id === id) ?? null;
  });

  const issuesOf = (node: TopologyNode) =>
    (data()?.issues ?? []).filter((issue) => issue.nodeIds.includes(node.id));

  const groupedIssues = createMemo(() => {
    const current = data();
    if (!current) return [];
    return SEVERITY_ORDER.map((severity) => ({
      severity,
      items: current.issues.filter((issue) => issue.severity === severity),
    })).filter((group) => group.items.length);
  });

  const counts = createMemo(() => {
    const bySeverity = new Map<TopologySeverity, number>();
    for (const issue of data()?.issues ?? []) {
      bySeverity.set(issue.severity, (bySeverity.get(issue.severity) ?? 0) + 1);
    }
    return bySeverity;
  });

  const openNode = (nodeIds: string[]) => {
    if (!nodeIds.length) return;
    setSelectedId(nodeIds[0]);
  };

  return (
    <div class="space-y-4">
      <div class="flex items-start justify-between flex-wrap gap-3">
        <div>
          <h1 class="text-2xl font-semibold">网络拓扑</h1>
          <p class="text-sm text-secondary mt-1">
            接入通道 → 路由 → 集群 → 目标端点，按当前配置实时组装，并顺带做一致性体检。
          </p>
        </div>
        <button class="btn btn-secondary" onClick={load} disabled={loading()}>
          <Show when={loading()} fallback={<FaSolidSync class="w-3.5 mr-1.5" />}>
            <FaSolidSpinner class="w-3.5 mr-1.5 animate-spin" />
          </Show>
          刷新
        </button>
      </div>

      <Show when={data()}>
        {(current) => (
          <div class="flex items-center gap-2 flex-wrap text-xs">
            <span class="px-2.5 py-1 rounded-full bg-gray-100 dark:bg-dark-200 text-secondary">
              接入通道 {current().summary.connectionCount}
            </span>
            <span class="px-2.5 py-1 rounded-full bg-gray-100 dark:bg-dark-200 text-secondary">
              路由 {current().summary.enabledRouteCount}/{current().summary.routeCount} 启用
            </span>
            <span class="px-2.5 py-1 rounded-full bg-gray-100 dark:bg-dark-200 text-secondary">
              集群 {current().summary.clusterCount}
            </span>
            <span class="px-2.5 py-1 rounded-full bg-gray-100 dark:bg-dark-200 text-secondary">
              端点 {current().summary.destinationCount - current().summary.unhealthyDestinationCount}
              /{current().summary.destinationCount} 健康
            </span>
            <For each={SEVERITY_ORDER}>
              {(severity) => (
                <Show when={(counts().get(severity) ?? 0) > 0}>
                  <span class={SEVERITY_META[severity].badge}>
                    {SEVERITY_META[severity].label} {counts().get(severity)}
                  </span>
                </Show>
              )}
            </For>
            <span class="text-secondary">
              生成于 {new Date(current().generatedAt).toLocaleTimeString()}
            </span>
          </div>
        )}
      </Show>

      <Show when={error()}>
        {(message) => (
          <div class="card border-danger/40">
            <div class="flex items-center gap-2 text-danger font-medium">
              <FaSolidTriangleExclamation class="w-4" />
              拓扑加载失败
            </div>
            <p class="text-sm text-secondary mt-2 break-all">{message()}</p>
            <button class="btn btn-primary mt-4" onClick={load}>重试</button>
          </div>
        )}
      </Show>

      <Show when={!error() && !data()}>
        <div class="card flex flex-col items-center justify-center gap-3 py-12 text-secondary">
          <FaSolidSpinner class="w-5 animate-spin" />
          <span>{loading() ? '正在计算拓扑…' : '暂无拓扑数据'}</span>
        </div>
      </Show>

      <Show when={!error() && data()}>
        <div class="flex flex-col xl:flex-row gap-4 items-start">
          <div class="card flex-1 min-w-0 w-full">
            <Show
              when={(data()?.summary.routeCount ?? 0) > 0}
              fallback={
                <div class="text-center py-10">
                  <p class="text-secondary">还没有路由，拓扑只有一条接入通道可画。</p>
                  <button class="btn btn-primary mt-4" onClick={() => navigate('/routes')}>
                    去创建路由
                  </button>
                </div>
              }
            >
              <div class="flex items-center gap-3 flex-wrap mb-4">
                <div class="relative flex-1 min-w-[200px]">
                  <FaSolidSearch class="w-3.5 h-3.5 absolute left-3 top-3 text-secondary pointer-events-none" />
                  <input
                    class="input pl-9"
                    placeholder="搜索路由、集群、域名或端点地址…"
                    value={search()}
                    onInput={(event) => setSearch(event.currentTarget.value)}
                  />
                </div>
                <label class="flex items-center gap-2 text-sm cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={onlyIssues()}
                    onChange={(event) => setOnlyIssues(event.currentTarget.checked)}
                  />
                  只看异常
                </label>
                <Show when={selectedId()}>
                  <button class="btn-text btn-text-primary" onClick={() => setSelectedId(null)}>
                    取消选中
                  </button>
                </Show>
              </div>

              <TopologyGraph
                nodes={visible().nodes}
                edges={visible().edges}
                selectedId={selectedId()}
                onSelect={setSelectedId}
              />

              <Show when={visible().nodes.length < (data()?.nodes.length ?? 0)}>
                <p class="text-xs text-secondary mt-3">
                  当前显示 {visible().nodes.length}/{data()?.nodes.length ?? 0} 个节点，
                  其余被搜索或"只看异常"过滤。
                </p>
              </Show>

              <p class="text-xs text-gray-400 dark:text-gray-500 mt-3">
                端点圆点为配置里的健康标记（控制面落库值），实时探测结果请到实例详情按实例查看。
              </p>
            </Show>
          </div>

          <div class="w-full xl:w-96 shrink-0 space-y-4">
            <Show
              when={selected()}
              fallback={
                <div class="card">
                  <div class="flex items-center justify-between">
                    <h2 class="font-medium">配置体检</h2>
                    <span class="text-xs text-secondary">
                      {data()?.issues.length ?? 0} 项
                    </span>
                  </div>
                  <Show
                    when={(data()?.issues.length ?? 0) > 0}
                    fallback={
                      <p class="text-sm text-success mt-3">
                        接入、路由、集群与端点之间没有发现断链或冲突。
                      </p>
                    }
                  >
                    <div class="mt-3 space-y-4">
                      <For each={groupedIssues()}>
                        {(group) => (
                          <div class="space-y-2">
                            <div class="flex items-center gap-2">
                              <span class={SEVERITY_META[group.severity].badge}>
                                {SEVERITY_META[group.severity].label}
                              </span>
                              <span class="text-xs text-secondary">{group.items.length} 项</span>
                            </div>
                            <For each={group.items}>
                              {(issue) => (
                                <button
                                  class="w-full text-left text-sm rounded-lg px-3 py-2 hover:bg-gray-100 dark:hover:bg-dark-200 transition-colors"
                                  onClick={() => openNode(issue.nodeIds)}
                                >
                                  {issue.message}
                                </button>
                              )}
                            </For>
                          </div>
                        )}
                      </For>
                    </div>
                  </Show>
                </div>
              }
            >
              {(node) => (
                <div class="card">
                  <div class="flex items-start justify-between gap-2">
                    <div class="min-w-0">
                      <div class="flex items-center gap-2">
                        <span class="text-xs text-secondary">{LAYER_TITLES[node().kind]}</span>
                        <span class={STATUS_BADGE[node().status]}>{STATUS_TEXT[node().status]}</span>
                      </div>
                      <h2 class="font-semibold mt-2 break-all">{node().label}</h2>
                      <Show when={node().subLabel}>
                        <p class="text-xs font-mono text-secondary mt-1 break-all">{node().subLabel}</p>
                      </Show>
                    </div>
                    <button
                      class="btn-icon btn-icon-primary shrink-0"
                      title="关闭详情"
                      onClick={() => setSelectedId(null)}
                    >
                      <FaSolidXmark />
                    </button>
                  </div>

                  <dl class="mt-4 space-y-2 text-sm">
                    <For each={node().details}>
                      {(detail) => (
                        <Show when={detail.value}>
                          <div class="flex items-start justify-between gap-3">
                            <dt class="text-secondary shrink-0">{detail.label}</dt>
                            <dd class="font-mono text-xs break-all text-right">{detail.value}</dd>
                          </div>
                        </Show>
                      )}
                    </For>
                  </dl>

                  <Show when={issuesOf(node()).length}>
                    <div class="mt-4 space-y-2">
                      <For each={issuesOf(node())}>
                        {(issue) => (
                          <div class="flex items-start gap-2 text-sm">
                            <span class={`${SEVERITY_META[issue.severity].badge} shrink-0`}>
                              {SEVERITY_META[issue.severity].label}
                            </span>
                            <span class="text-secondary">{issue.message}</span>
                          </div>
                        )}
                      </For>
                    </div>
                  </Show>

                  <button
                    class="btn btn-secondary w-full mt-5"
                    onClick={() => navigate(LAYER_LINKS[node().kind])}
                  >
                    到{LAYER_TITLES[node().kind]}页处理
                  </button>
                </div>
              )}
            </Show>
          </div>
        </div>
      </Show>
    </div>
  );
}
