import { createSignal, For } from 'solid-js';
import { api } from '../api/client';
import type { AccessLog } from '../types';

export default function LogsPage() {
  const [logs, setLogs] = createSignal<AccessLog[]>([]);
  const [search, setSearch] = createSignal('');

  const handleSearch = async () => {
    try {
      const data = await api.get<AccessLog[]>(`/logs/access?pageSize=50&message=${search()}`);
      setLogs(data || []);
    } catch { /* ignore */ }
  };

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
            <i class="fa fa-search absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400"></i>
          </div>
          <div class="flex space-x-2">
            <button class="btn btn-secondary" onClick={handleSearch}>
              <i class="fa fa-refresh mr-2"></i>查询
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
              <p>点击"查询"查看日志</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
