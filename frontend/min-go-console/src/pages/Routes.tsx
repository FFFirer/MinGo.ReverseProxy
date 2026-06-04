import { createSignal, onMount, For } from 'solid-js';
import { api } from '../api/client';
import { addToast } from '../store/toast';
import type { RouteConfig, ClusterConfig } from '../types';

export default function RoutesPage() {
  const [routes, setRoutes] = createSignal<RouteConfig[]>([]);
  const [clusters, setClusters] = createSignal<ClusterConfig[]>([]);
  const [search, setSearch] = createSignal('');
  const [filterEnabled, setFilterEnabled] = createSignal<string>('all');
  const [editingRoute, setEditingRoute] = createSignal<RouteConfig | null>(null);
  const [showModal, setShowModal] = createSignal(false);
  const [loading, setLoading] = createSignal(true);

  onMount(async () => {
    try {
      const [r, c] = await Promise.all([
        api.get<RouteConfig[]>('/apimanagement/routes'),
        api.get<ClusterConfig[]>('/apimanagement/clusters'),
      ]);
      setRoutes(r);
      setClusters(c);
    } catch (err) {
      addToast('error', '加载路由或集群列表失败');
    } finally {
      setLoading(false);
    }
  });

  const filteredRoutes = () => routes().filter(r => {
    if (filterEnabled() !== 'all') {
      const match = filterEnabled() === 'enabled';
      if (r.enabled !== match) return false;
    }
    if (search()) {
      return r.name.includes(search()) || (r.match?.path || '').includes(search());
    }
    return true;
  });

  const handleSave = async (route: RouteConfig) => {
    try {
      if (route.id) {
        await api.put(`/apimanagement/routes/${route.id}`, route);
        addToast('success', '路由已更新');
      } else {
        await api.post('/apimanagement/routes', route);
        addToast('success', '路由已创建');
      }
      setRoutes(await api.get<RouteConfig[]>('/apimanagement/routes'));
      setShowModal(false);
    } catch (err) {
      addToast('error', `保存路由失败: ${(err as Error).message}`);
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm('确定删除此路由？')) return;
    try {
      await api.delete(`/apimanagement/routes/${id}`);
      setRoutes(await api.get<RouteConfig[]>('/apimanagement/routes'));
      addToast('success', '路由已删除');
    } catch (err) {
      addToast('error', `删除路由失败: ${(err as Error).message}`);
    }
  };

  return (
    <div>
      <div class="mb-6 flex items-center justify-between">
        <div>
          <h2 class="text-2xl font-bold mb-2">路由管理</h2>
          <p class="text-secondary">配置和管理API路由规则</p>
        </div>
        <button class="btn btn-primary" onClick={() => { setEditingRoute(null); setShowModal(true); }}>
          <i class="fa fa-plus mr-2"></i>添加路由
        </button>
      </div>

      <div class="card">
        <div class="flex flex-col md:flex-row md:items-center md:justify-between mb-4 gap-4">
          <div class="relative flex-1 max-w-md">
            <input type="text" placeholder="搜索路由..." class="input pl-10"
              value={search()} onInput={(e) => setSearch(e.currentTarget.value)} />
            <i class="fa fa-search absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400"></i>
          </div>
          <select class="input w-auto" value={filterEnabled()} onChange={(e) => setFilterEnabled(e.currentTarget.value)}>
            <option value="all">所有状态</option>
            <option value="enabled">启用</option>
            <option value="disabled">禁用</option>
          </select>
        </div>

        <div class="overflow-x-auto">
          <table class="w-full">
            <thead>
              <tr class="border-b border-gray-200 dark:border-dark-200">
                <th class="text-left py-3 px-4 font-medium text-secondary">路由名称</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">路径</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">集群</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">状态</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">操作</th>
              </tr>
            </thead>
            <tbody>
              <For each={filteredRoutes()}>{(route) => (
                <tr class="border-b border-gray-100 dark:border-dark-100 hover:bg-gray-50 dark:hover:bg-dark-100/50">
                  <td class="py-3 px-4 font-medium">{route.name}</td>
                  <td class="py-3 px-4 font-mono text-sm">{route.match?.path || '-'}</td>
                  <td class="py-3 px-4">{route.clusterId}</td>
                  <td class="py-3 px-4">
                    <span class={`badge ${route.enabled ? 'badge-success' : 'badge-warning'}`}>
                      {route.enabled ? '启用' : '禁用'}
                    </span>
                  </td>
                  <td class="py-3 px-4">
                    <div class="flex space-x-2">
                      <button class="text-primary hover:text-primary/80" onClick={() => { setEditingRoute(route); setShowModal(true); }}>
                        <i class="fa fa-edit"></i>
                      </button>
                      <button class="text-danger hover:text-danger/80" onClick={() => handleDelete(route.id)}>
                        <i class="fa fa-trash"></i>
                      </button>
                    </div>
                  </td>
                </tr>
              )}</For>
              {!loading() && filteredRoutes().length === 0 && (
                <tr><td colspan="5" class="py-8 text-center text-secondary">暂无路由</td></tr>
              )}
              {loading() && (
                <tr><td colspan="5" class="py-8 text-center text-secondary">加载中...</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {showModal() && (
        <RouteFormModal
          route={editingRoute()}
          clusters={clusters()}
          onSave={handleSave}
          onClose={() => setShowModal(false)}
        />
      )}
    </div>
  );
}

function RouteFormModal(props: {
  route: RouteConfig | null;
  clusters: ClusterConfig[];
  onSave: (r: RouteConfig) => void;
  onClose: () => void;
}) {
  const [name, setName] = createSignal(props.route?.name || '');
  const [path, setPath] = createSignal(props.route?.match?.path || '');
  const [clusterId, setClusterId] = createSignal(props.route?.clusterId || '');
  const [enabled, setEnabled] = createSignal(props.route?.enabled ?? true);
  const [errors, setErrors] = createSignal<Record<string, string>>({});

  const validate = (): boolean => {
    const errs: Record<string, string> = {};
    if (!name().trim()) errs.name = '路由名称不能为空';
    if (!path().trim()) errs.path = '匹配路径不能为空';
    if (!clusterId()) errs.clusterId = '请选择目标集群';
    setErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const handleSubmit = (e: Event) => {
    e.preventDefault();
    if (!validate()) return;
    props.onSave({
      id: props.route?.id || '',
      name: name(),
      clusterId: clusterId(),
      match: { path: path() },
      enabled: enabled(),
    });
  };

  return (
    <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={props.onClose}>
      <div class="card w-full max-w-lg mx-4" onClick={(e) => e.stopPropagation()}>
        <h3 class="font-semibold mb-4">{props.route ? '编辑路由' : '添加路由'}</h3>
        <form onSubmit={handleSubmit} class="space-y-4">
          <div>
            <label class="block text-sm font-medium mb-1">路由名称</label>
            <input class="input" value={name()} onInput={(e) => setName(e.currentTarget.value)} />
            {errors().name && <p class="text-danger text-xs mt-1">{errors().name}</p>}
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">匹配路径</label>
            <input class="input" value={path()} onInput={(e) => setPath(e.currentTarget.value)} placeholder="/api/{**catch-all}" />
            {errors().path && <p class="text-danger text-xs mt-1">{errors().path}</p>}
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">目标集群</label>
            <select
              class="input"
              value={clusterId()}
              onChange={(e) => setClusterId(e.currentTarget.value)}
            >
              <option value="">-- 请选择集群 --</option>
              <For each={props.clusters}>{(c) => (
                <option value={c.id}>{c.name || c.id}</option>
              )}</For>
            </select>
            {errors().clusterId && <p class="text-danger text-xs mt-1">{errors().clusterId}</p>}
          </div>
          <div class="flex items-center space-x-2">
            <input type="checkbox" id="enabled" checked={enabled()} onChange={(e) => setEnabled(e.currentTarget.checked)} />
            <label for="enabled">启用</label>
          </div>
          <div class="flex justify-end space-x-2">
            <button type="button" class="btn btn-secondary" onClick={props.onClose}>取消</button>
            <button type="submit" class="btn btn-primary">保存</button>
          </div>
        </form>
      </div>
    </div>
  );
}
