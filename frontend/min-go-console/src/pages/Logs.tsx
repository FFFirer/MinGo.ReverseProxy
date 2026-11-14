import { createSignal, createEffect, onMount, For, Show } from 'solid-js';
import { useSearchParams } from '@solidjs/router';
import { api } from '../api/client';
import type { AccessLog, ServiceMetrics } from '../types';
import { FaSolidSearch, FaSolidRefresh, FaSolidChevronRight, FaSolidChevronDown } from 'solid-icons/fa';

function Waterfall(props: { log: AccessLog }) {
  const total = () => Math.max(props.log.durationMs, 1);
  const ttfb = () => Math.min(props.log.ttfbMs ?? 0, total());
  const download = () => Math.min(props.log.downloadMs ?? 0, total());
  const segments = () => [
    { label: '网关处理', ms: Math.max(0, total() - ttfb() - download()), color: 'bg-gray-500' },
    { label: '等待上游响应', ms: ttfb(), color: 'bg-success' },
    { label: '内容下载', ms: download(), color: 'bg-primary' },
  ];

  return (
    <div class="font-sans">
      <div class="flex h-3 w-full rounded overflow-hidden bg-gray-700/30">
        <For each={segments()}>{(s) => (
          <Show when={s.ms > 0}>
            <div
              class={s.color}
              style={{ width: `${(s.ms / total()) * 100}%`, 'min-width': '2px' }}
              title={`${s.label} ${s.ms}ms`}
            />
          </Show>
        )}</For>
      </div>
      <div class="flex flex-wrap items-center gap-x-4 gap-y-1 mt-2 text-xs text-secondary">
        <For each={segments()}>{(s) => (
          <span class="inline-flex items-center gap-1">
            <span class={`w-2 h-2 rounded-sm ${s.color}`} />
            {s.label} {s.ms}ms
          </span>
        )}</For>
        <span class="ml-auto">总耗时 {props.log.durationMs}ms</span>
      </div>
    </div>
  );
}

export default function LogsPage() {
  const [params, setParams] = useSearchParams();
  const [logs, setLogs] = createSignal<AccessLog[]>([]);
  const [services, setServices] = createSignal<ServiceMetrics[]>([]);
  const [search, setSearch] = createSignal('');
  const [expandedId, setExpandedId] = createSignal('');

  const fetchLogs = async () => {
    try {
      const qp = new URLSearchParams({ pageSize: '200' });
      if (params.q) qp.set('message', params.q as string);
      if (params.routeId) qp.set('routeId', params.routeId as string);
      const status = params.status as string | undefined;
      if (status) {
        if (status.endsWith('xx')) qp.set('statusClass', status);
        else qp.set('statusCode', status);
      }
      const range = params.range as string | undefined;
      if (range && range !== 'all') {
        const hours = range === '1h' ? 1 : 24;
        qp.set('start', new Date(Date.now() - hours * 3600000).toISOString());
        qp.set('end', new Date().toISOString());
      }
      const data = await api.get<AccessLog[]>(`/logs/access?${qp}`);
      setLogs(data || []);
    } catch { /* ignore */ }
  };

  onMount(() => {
    setSearch((params.q as string) ?? '');
    api.get<ServiceMetrics[]>('/monitoring/services')
      .then((s) => setServices(s || []))
      .catch(() => setServices([]));
  });

  // 过滤条件全部落在 URL query 上：指标页下钻链接可直接命中，且刷新/分享不丢状态
  createEffect(() => {
    void params.q;
    void params.routeId;
    void params.status;
    void params.range;
    fetchLogs();
  });

  const handleSearch = () => setParams({ q: search() || undefined });

  const logClass = (status: number) => {
    if (status >= 500) return 'text-danger';
    if (status >= 400) return 'text-warning';
    return 'text-gray-600 dark:text-gray-400';
  };

  const routeName = (id?: string) =>
    (id && services().find((s) => s.serviceId === id)?.serviceName) || id || '-';

  const statusIsExactCode = () => {
    const s = params.status as string | undefined;
    return !!s && !s.endsWith('xx');
  };

  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">日志管理</h2>
        <p class="text-secondary">查看和分析网关访问日志</p>
      </div>

      <div class="card">
        <div class="flex flex-col lg:flex-row lg:items-center gap-3 mb-4">
          <div class="relative flex-1 max-w-md">
            <input type="text" placeholder="搜索路径..." class="input pl-10"
              value={search()} onInput={(e) => setSearch(e.currentTarget.value)} onKeyDown={(e) => e.key === 'Enter' && handleSearch()} />
            <FaSolidSearch class="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" />
          </div>
          <div class="flex flex-wrap items-center gap-2">
            <select class="input w-auto" value={(params.routeId as string) ?? ''}
              onChange={(e) => setParams({ routeId: e.currentTarget.value || undefined })}>
              <option value="">全部路由</option>
              <For each={services()}>{(s) => (
                <option value={s.serviceId}>{s.serviceName}</option>
              )}</For>
            </select>
            <select class="input w-auto" value={(params.status as string) ?? ''}
              onChange={(e) => setParams({ status: e.currentTarget.value || undefined })}>
              <option value="">全部状态</option>
              <option value="2xx">2xx</option>
              <option value="4xx">4xx</option>
              <option value="5xx">5xx</option>
              <Show when={statusIsExactCode()}>
                <option value={params.status as string}>{params.status}</option>
              </Show>
            </select>
            <select class="input w-auto" value={(params.range as string) ?? ''}
              onChange={(e) => setParams({ range: e.currentTarget.value || undefined })}>
              <option value="">全部时间</option>
              <option value="1h">最近1小时</option>
              <option value="24h">最近24小时</option>
            </select>
            <button class="btn btn-secondary" onClick={handleSearch}>
              <FaSolidRefresh class="mr-2" />查询
            </button>
          </div>
        </div>

        <div class="bg-gray-50 dark:bg-dark-200 rounded-lg p-4 max-h-[32rem] overflow-y-auto font-mono text-sm">
          {logs().length > 0 ? (
            <For each={logs()}>{(log) => (
              <div class="mb-1">
                <div class={`flex items-center gap-2 cursor-pointer hover:bg-gray-200/40 dark:hover:bg-dark-300/40 rounded px-1 py-0.5 ${logClass(log.statusCode)}`}
                  onClick={() => setExpandedId(expandedId() === log.id ? '' : log.id)}>
                  <span class="text-secondary shrink-0">
                    {expandedId() === log.id ? <FaSolidChevronDown /> : <FaSolidChevronRight />}
                  </span>
                  <span class="shrink-0">{new Date(log.timestamp).toLocaleTimeString()}</span>
                  <span class="shrink-0">[{log.method}]</span>
                  <span class="truncate">{log.path}</span>
                  <span class="shrink-0 font-bold">{log.statusCode}</span>
                  <span class="shrink-0">{log.durationMs}ms</span>
                  <span class="shrink-0 text-secondary ml-auto">{log.clientIp}</span>
                </div>
                <Show when={expandedId() === log.id}>
                  <div class="ml-6 mt-1 mb-2 pl-3 border-l-2 border-gray-500/40">
                    <Waterfall log={log} />
                    <div class="font-sans text-xs text-secondary mt-2">
                      路由 {routeName(log.routeId)}
                      <Show when={log.routeId && routeName(log.routeId) !== log.routeId}>
                        <span class="font-mono ml-1">({log.routeId})</span>
                      </Show>
                      <Show when={log.instanceId}> · 实例 {log.instanceId}</Show>
                    </div>
                  </div>
                </Show>
              </div>
            )}</For>
          ) : (
            <div class="text-center text-secondary py-8">
              <p>暂无访问日志</p>
              <p class="text-xs mt-1">日志将在网关处理请求后显示</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
