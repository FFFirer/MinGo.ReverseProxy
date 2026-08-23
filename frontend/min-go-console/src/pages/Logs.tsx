import { createSignal, For, onMount } from 'solid-js';
import { api } from '../api/client';
import type { AccessLog } from '../types';
import { FaSolidSearch, FaSolidRefresh } from 'solid-icons/fa';

export default function LogsPage() {
  const [logs, setLogs] = createSignal<AccessLog[]>([]);
  const [search, setSearch] = createSignal('');

  const fetchLogs = async (message = '') => {
    try {
      const params = new URLSearchParams({ pageSize: '100' });
      if (message) params.set('message', message);
      const data = await api.get<AccessLog[]>(`/logs/access?${params}`);
      setLogs(data || []);
    } catch { /* ignore */ }
  };

  onMount(() => fetchLogs());

  const handleSearch = () => fetchLogs(search());

  const logClass = (status: number) => {
    if (status >= 500) return 'text-danger';
    if (status >= 400) return 'text-warning';
    return 'text-gray-600 dark:text-gray-400';
  };

  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">日志管理</h2>
        <p class="text-secondary">查看和分析网关访问日志</p>
      </div>

      <div class="card">
        <div class="flex flex-col md:flex-row md:items-center md:justify-between mb-4 gap-4">
          <div class="relative flex-1 max-w-md">
            <input type="text" placeholder="搜索日志..." class="input pl-10"
              value={search()} onInput={(e) => setSearch(e.currentTarget.value)} onKeyDown={(e) => e.key === 'Enter' && handleSearch()} />
            <FaSolidSearch class="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" />
          </div>
          <div class="flex space-x-2">
            <button class="btn btn-secondary" onClick={handleSearch}>
              <FaSolidRefresh class="mr-2" />查询
            </button>
          </div>
        </div>

        <div class="bg-gray-50 dark:bg-dark-200 rounded-lg p-4 max-h-96 overflow-y-auto font-mono text-sm">
          {logs().length > 0 ? (
            <For each={logs()}>{(log) => (
              <div class={`mb-2 ${logClass(log.statusCode)}`}>
                {new Date(log.timestamp).toLocaleTimeString()} [{log.method}] {log.path} {log.statusCode} {log.durationMs}ms {log.clientIp}
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
