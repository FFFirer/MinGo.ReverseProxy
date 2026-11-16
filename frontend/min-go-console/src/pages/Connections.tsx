import { createSignal, onMount, Show, For } from 'solid-js';
import { addToast } from '../store/toast';
import { api } from '../api/client';
import type {
  CloudflareStatus,
  CloudflareTunnel,
  TunnelConnection,
  TunnelIngressView,
  ReconcileReport,
} from '../types';
import type { Column } from '../components/shared/Table';
import Table from '../components/shared/Table';
import {
  FaSolidServer,
  FaSolidPlus,
  FaSolidEdit,
  FaSolidTrash,
  FaSolidTimes,
  FaSolidSync,
  FaSolidSpinner,
  FaSolidInfoCircle,
  FaSolidTriangleExclamation,
} from 'solid-icons/fa';

const TUNNEL_STATUS_LABELS: Record<string, string> = {
  healthy: '健康',
  degraded: '降级',
  down: '断连',
  inactive: '未启动',
};

function tunnelStatusBadge(status?: string | null) {
  if (!status) return <span class="badge badge-warning">未知</span>;
  const label = TUNNEL_STATUS_LABELS[status] || status;
  const cls =
    status === 'healthy' ? 'badge-success'
    : status === 'degraded' ? 'badge-warning'
    : status === 'down' ? 'badge-danger'
    : 'badge-warning';
  return <span class={`badge ${cls}`}>{label}</span>;
}

export default function ConnectionsPage() {
  const [status, setStatus] = createSignal<CloudflareStatus | null>(null);
  const [tunnels, setTunnels] = createSignal<CloudflareTunnel[]>([]);
  const [loading, setLoading] = createSignal(true);

  const [showCreate, setShowCreate] = createSignal(false);
  const [renaming, setRenaming] = createSignal<CloudflareTunnel | null>(null);
  const [detailTunnel, setDetailTunnel] = createSignal<CloudflareTunnel | null>(null);

  const load = async () => {
    setLoading(true);
    try {
      const [s, t] = await Promise.all([api.getCloudflareStatus(), api.getCloudflareTunnels()]);
      setStatus(s);
      setTunnels(t);
    } catch (err) {
      addToast('error', `加载 Cloudflare 信息失败: ${(err as Error).message}`);
    } finally {
      setLoading(false);
    }
  };

  onMount(load);

  const handleDelete = async (tunnel: CloudflareTunnel) => {
    if (!confirm(`确定删除隧道 "${tunnel.name}"？Cloudflare 侧将清理其全部 ingress 配置。`)) return;
    try {
      await api.deleteCloudflareTunnel(tunnel.id);
      addToast('success', `已删除隧道 "${tunnel.name}"`);
      await load();
    } catch (err) {
      addToast('error', `删除失败: ${(err as Error).message}`);
    }
  };

  const columns: Column<CloudflareTunnel>[] = [
    {
      key: 'name',
      title: '名称',
      mobileVisible: true,
      cell: (t) => <span class="font-medium">{t.name}</span>,
    },
    {
      key: 'id',
      title: '隧道 ID',
      cell: (t) => <span class="font-mono text-xs break-all">{t.id}</span>,
    },
    {
      key: 'status',
      title: '状态',
      mobileVisible: true,
      cell: (t) => tunnelStatusBadge(t.status),
    },
    {
      key: 'connsActiveAt',
      title: '最近活跃',
      cell: (t) => (
        <span class="text-xs text-secondary">
          {t.connsActiveAt ? new Date(t.connsActiveAt).toLocaleString() : '-'}
        </span>
      ),
    },
    {
      key: 'actions',
      title: '操作',
      mobileVisible: true,
      mobileDetailVisible: false,
      class: 'w-28',
      cell: (t) => (
        <div class="flex items-center justify-end gap-1">
          <button
            class="btn-text btn-text-primary"
            onClick={(e) => { e.stopPropagation(); setDetailTunnel(t); }}
          >
            详情
          </button>
          <button
            class="btn-icon btn-icon-primary"
            title="重命名"
            onClick={(e) => { e.stopPropagation(); setRenaming(t); }}
          >
            <FaSolidEdit />
          </button>
          <button
            class="btn-icon btn-icon-danger"
            title="删除"
            onClick={(e) => { e.stopPropagation(); handleDelete(t); }}
          >
            <FaSolidTrash />
          </button>
        </div>
      ),
    },
  ];

  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">接入通道</h2>
        <p class="text-secondary">流量进入网关的通道：内置本地数据面与 Cloudflare 隧道并列，路由按需选择发布通道</p>
      </div>

      {/* 本地数据面 */}
      <div class="card">
        <div class="flex items-center justify-between gap-4">
          <div class="flex items-center gap-4">
            <div class="w-10 h-10 rounded-lg bg-primary/10 text-primary flex items-center justify-center">
              <FaSolidServer class="w-5 h-5" />
            </div>
            <div>
              <h3 class="font-semibold">本地数据面</h3>
              <p class="text-sm text-secondary mt-0.5">内置通道 · 监听 8080 (HTTP) / 8443 (HTTPS)</p>
            </div>
          </div>
          <span class="badge badge-success shrink-0">内置</span>
        </div>
      </div>

      {/* Cloudflare 隧道 */}
      <div class="card mt-6">
        <div class="flex items-center justify-between gap-4 mb-4">
          <div>
            <h3 class="font-semibold">Cloudflare 隧道</h3>
            <p class="text-sm text-secondary mt-1">
              通过 Cloudflare API 管理隧道资源，发布到隧道的路由自动维护 CNAME 与 ingress。
            </p>
          </div>
          <button
            class="btn btn-primary shrink-0"
            disabled={!status()?.configured}
            onClick={() => setShowCreate(true)}
          >
            <FaSolidPlus class="mr-2" />添加隧道
          </button>
        </div>

        <Show when={status() && !status()!.configured}>
          <div class="mb-4 p-3 rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 text-sm flex items-start gap-2">
            <FaSolidInfoCircle class="w-4 h-4 mt-0.5 shrink-0 text-amber-600 dark:text-amber-400" />
            <div>
              <p>Cloudflare 连接未配置：在控制面配置 <code class="font-mono text-xs">Cloudflare:ApiToken</code>（环境变量 <code class="font-mono text-xs">Cloudflare__ApiToken</code>）后刷新。</p>
              <p class="text-secondary mt-1">所需权限：Account:Cloudflare Tunnel:Edit、Zone:DNS:Edit、Zone:Zone:Read。</p>
            </div>
          </div>
        </Show>

        <Show when={status()?.configured && status()!.error}>
          <div class="mb-4 p-3 rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-sm flex items-start gap-2">
            <FaSolidTriangleExclamation class="w-4 h-4 mt-0.5 shrink-0 text-red-600 dark:text-red-400" />
            <p class="break-all">Cloudflare 连接失败：{status()!.error}</p>
          </div>
        </Show>

        <Show when={status()?.configured && !status()!.error}>
          <div class="mb-4 p-3 rounded-lg bg-emerald-50 dark:bg-emerald-900/20 border border-emerald-200 dark:border-emerald-800 text-sm flex items-start gap-2">
            <FaSolidInfoCircle class="w-4 h-4 mt-0.5 shrink-0 text-emerald-600 dark:text-emerald-400" />
            <p>
              已连接{status()!.accountName ? ` · 账号 ${status()!.accountName}` : ''}
              {status()!.zonesCount != null ? ` · ${status()!.zonesCount} 个域名区` : ''}
            </p>
          </div>
        </Show>

        <Table
          columns={columns}
          data={tunnels()}
          rowKey={(t) => t.id}
          emptyMessage={loading() ? '加载中...' : '暂无隧道'}
        />
      </div>

      <Show when={showCreate()}>
        <TunnelNameModal
          title="添加 Cloudflare 隧道"
          initialName=""
          onSave={async (name) => {
            await api.createCloudflareTunnel(name);
            addToast('success', `已创建隧道 "${name}"`);
            setShowCreate(false);
            await load();
          }}
          onClose={() => setShowCreate(false)}
        />
      </Show>

      <Show when={renaming()}>
        <TunnelNameModal
          title={`重命名隧道 "${renaming()!.name}"`}
          initialName={renaming()!.name}
          onSave={async (name) => {
            await api.updateCloudflareTunnel(renaming()!.id, name);
            addToast('success', '隧道已重命名');
            setRenaming(null);
            await load();
          }}
          onClose={() => setRenaming(null)}
        />
      </Show>

      <Show when={detailTunnel()}>
        <TunnelDetailModal tunnel={detailTunnel()!} onClose={() => setDetailTunnel(null)} />
      </Show>
    </div>
  );
}

// ─── 名称表单 Modal ───

function TunnelNameModal(props: {
  title: string;
  initialName: string;
  onSave: (name: string) => Promise<void>;
  onClose: () => void;
}) {
  const [name, setName] = createSignal(props.initialName);
  const [error, setError] = createSignal('');
  const [submitting, setSubmitting] = createSignal(false);

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    if (!name().trim()) {
      setError('隧道名称不能为空');
      return;
    }
    setSubmitting(true);
    try {
      await props.onSave(name().trim());
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={props.onClose}>
      <div class="card w-full max-w-md mx-4" onClick={(e) => e.stopPropagation()}>
        <div class="flex items-center justify-between mb-4">
          <h3 class="font-semibold">{props.title}</h3>
          <button onClick={props.onClose} class="text-secondary hover:text-gray-800 dark:hover:text-gray-200">
            <FaSolidTimes />
          </button>
        </div>
        <form onSubmit={handleSubmit} class="space-y-4">
          <div>
            <label class="block text-sm font-medium mb-1">隧道名称</label>
            <input
              class="input"
              value={name()}
              onInput={(e) => { setName(e.currentTarget.value); setError(''); }}
              placeholder="例如 home-internal"
              autofocus
            />
            {error() && <p class="text-danger text-xs mt-1">{error()}</p>}
          </div>
          <div class="flex justify-end space-x-2">
            <button type="button" class="btn btn-secondary" onClick={props.onClose}>取消</button>
            <button type="submit" class="btn btn-primary" disabled={submitting()}>
              {submitting() ? '保存中...' : '保存'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ─── 隧道详情 Modal ───

function TunnelDetailModal(props: { tunnel: CloudflareTunnel; onClose: () => void }) {
  const [view, setView] = createSignal<TunnelIngressView | null>(null);
  const [connections, setConnections] = createSignal<TunnelConnection[]>([]);
  const [report, setReport] = createSignal<ReconcileReport | null>(null);
  const [loading, setLoading] = createSignal(true);
  const [reconciling, setReconciling] = createSignal(false);

  const load = async () => {
    setLoading(true);
    try {
      const [v, c] = await Promise.all([
        api.getTunnelIngress(props.tunnel.id),
        api.getTunnelConnections(props.tunnel.id),
      ]);
      setView(v);
      setConnections(c);
    } catch (err) {
      addToast('error', `加载隧道详情失败: ${(err as Error).message}`);
    } finally {
      setLoading(false);
    }
  };

  onMount(load);

  const handleReconcile = async () => {
    setReconciling(true);
    setReport(null);
    try {
      const r = await api.reconcileTunnel(props.tunnel.id);
      setReport(r);
      if (r.conflicts.length > 0) {
        addToast('warning', `对账完成，有 ${r.conflicts.length} 项冲突需处理`);
      } else {
        addToast('success', '对账完成，发布状态已与本地路由一致');
      }
      await load();
    } catch (err) {
      addToast('error', `对账失败: ${(err as Error).message}`);
    } finally {
      setReconciling(false);
    }
  };

  const ruleColumns: Column<{ hostname: string; service: string; managed: boolean }>[] = [
    {
      key: 'hostname',
      title: '域名',
      mobileVisible: true,
      cell: (r) => <span class="font-mono text-xs">{r.hostname || <span class="text-secondary">(兜底)</span>}</span>,
    },
    {
      key: 'service',
      title: '回源',
      cell: (r) => <span class="font-mono text-xs break-all">{r.service}</span>,
    },
    {
      key: 'managed',
      title: '归属',
      cell: (r) => (
        r.hostname
          ? (r.managed
              ? <span class="badge badge-success text-xs">MinGo 托管</span>
              : <span class="badge badge-warning text-xs">CF 面板手工</span>)
          : <span class="badge bg-gray-100 text-gray-500 dark:bg-dark-200 dark:text-gray-400 text-xs">兜底规则</span>
      ),
    },
  ];

  return (
    <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={props.onClose}>
      <div class="card w-full max-w-3xl mx-4 max-h-[90vh] overflow-y-auto" onClick={(e) => e.stopPropagation()}>
        <div class="flex items-center justify-between mb-4">
          <div>
            <h3 class="font-semibold">{props.tunnel.name}</h3>
            <p class="text-xs text-secondary font-mono mt-0.5">{props.tunnel.id}</p>
          </div>
          <button onClick={props.onClose} class="text-secondary hover:text-gray-800 dark:hover:text-gray-200">
            <FaSolidTimes />
          </button>
        </div>

        <Show when={loading()}>
          <div class="text-sm text-secondary py-6 text-center">正在加载...</div>
        </Show>

        <Show when={!loading() && view()}>
          <div class="space-y-5">
            {/* 连接状态 */}
            <div>
              <h4 class="text-sm font-semibold mb-2">连接器（{connections().length}）</h4>
              <Show when={connections().length === 0} fallback={
                <div class="space-y-1.5">
                  <For each={connections()}>{(c) => (
                    <div class="flex items-center justify-between bg-gray-50 dark:bg-dark-200 rounded px-3 py-2 text-xs">
                      <span class="font-mono">{c.id.slice(0, 8)}…</span>
                      <span class="text-secondary">{c.coloName || '-'}</span>
                      <span class="text-secondary">{c.clientVersion || '-'}</span>
                      <span class="text-secondary">
                        {c.openedAt ? new Date(c.openedAt).toLocaleString() : '-'}
                      </span>
                    </div>
                  )}</For>
                </div>
              }>
                <p class="text-xs text-secondary">无活跃连接 —— 隧道尚未有 cloudflared 连接器接入，外部流量不可达。</p>
              </Show>
            </div>

            {/* 对账视图 */}
            <div>
              <div class="flex items-center justify-between mb-2">
                <h4 class="text-sm font-semibold">Ingress 规则与本地路由对账</h4>
                <button
                  class="btn btn-secondary btn-sm"
                  disabled={reconciling()}
                  onClick={handleReconcile}
                >
                  <Show when={reconciling()} fallback={<FaSolidSync class="mr-1.5" />}>
                    <FaSolidSpinner class="mr-1.5 animate-spin" />
                  </Show>
                  重对账
                </button>
              </div>

              <Show when={view()!.missingHosts.length > 0}>
                <div class="mb-2 p-2.5 rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 text-xs">
                  以下域名绑定了本地路由但未出现在 ingress（漂移）：{view()!.missingHosts.join(', ')}
                  <span class="text-secondary"> —— 点击"重对账"修复</span>
                </div>
              </Show>

              <Table
                columns={ruleColumns}
                data={view()!.rules.map((r) => ({
                  hostname: r.hostname,
                  service: r.service,
                  managed: view()!.localBindings.some(
                    (b) => b.enabled && b.host.toLowerCase() === r.hostname.toLowerCase(),
                  ),
                }))}
                rowKey={(r) => r.hostname || r.service}
                emptyMessage="暂无 ingress 规则"
              />

              <Show when={view()!.localBindings.length > 0}>
                <h5 class="text-xs font-semibold text-secondary mt-3 mb-1.5">本地绑定路由</h5>
                <div class="space-y-1">
                  <For each={view()!.localBindings}>{(b) => (
                    <div class="flex items-center justify-between text-xs px-3 py-1.5 bg-gray-50 dark:bg-dark-200 rounded">
                      <span>{b.routeName}</span>
                      <span class="font-mono text-secondary">{b.host}</span>
                      <span class={`badge text-xs ${b.enabled ? 'badge-success' : 'badge-warning'}`}>
                        {b.enabled ? '启用' : '禁用'}
                      </span>
                    </div>
                  )}</For>
                </div>
              </Show>
            </div>

            {/* 对账报告 */}
            <Show when={report()}>
              <div class="p-3 rounded-lg bg-gray-50 dark:bg-dark-200 text-xs space-y-1">
                <p class="font-medium">最近对账</p>
                <Show when={report()!.ingressUpdated}>
                  <p class="text-secondary">ingress 已更新</p>
                </Show>
                <Show when={report()!.dnsCreated.length > 0}>
                  <p>新增 CNAME：{report()!.dnsCreated.join(', ')}</p>
                </Show>
                <Show when={report()!.dnsDeleted.length > 0}>
                  <p>删除 CNAME：{report()!.dnsDeleted.join(', ')}</p>
                </Show>
                <Show when={report()!.dnsKept.length > 0}>
                  <p class="text-secondary">保留非本隧道 CNAME：{report()!.dnsKept.join(', ')}</p>
                </Show>
                <Show when={report()!.conflicts.length > 0}>
                  <p class="text-danger">冲突：{report()!.conflicts.join('；')}</p>
                </Show>
                <Show when={!report()!.ingressUpdated && report()!.dnsCreated.length === 0 && report()!.dnsDeleted.length === 0 && report()!.conflicts.length === 0}>
                  <p class="text-secondary">无变化，已与本地路由一致</p>
                </Show>
              </div>
            </Show>
          </div>
        </Show>
      </div>
    </div>
  );
}
