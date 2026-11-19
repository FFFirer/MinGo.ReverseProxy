import { createSignal, onMount, For, Show, createMemo } from 'solid-js';
import { api } from '../api/client';
import { addToast } from '../store/toast';
import type { RouteConfig, ClusterConfig, TransformSchema, TransformFieldSchema, ConnectionSummary, RouteSaveResponse } from '../types';
import { FaSolidPlus, FaSolidSearch, FaSolidChevronDown, FaSolidChevronRight, FaSolidTrash } from 'solid-icons/fa';
import type { Column } from '../components/shared/Table';
import Table from '../components/shared/Table';

export default function RoutesPage() {
  const [routes, setRoutes] = createSignal<RouteConfig[]>([]);
  const [clusters, setClusters] = createSignal<ClusterConfig[]>([]);
  const [connections, setConnections] = createSignal<ConnectionSummary[]>([]);
  const [search, setSearch] = createSignal('');
  const [filterEnabled, setFilterEnabled] = createSignal<string>('all');
  const [editingRoute, setEditingRoute] = createSignal<RouteConfig | null>(null);
  const [showModal, setShowModal] = createSignal(false);
  const [loading, setLoading] = createSignal(true);
  const [schemas, setSchemas] = createSignal<TransformSchema[]>([]);

  const connectionName = (id?: string) => {
    if (!id || id === 'local') return '本地';
    const conn = connections().find((c) => c.id === id);
    return conn?.name || id.slice(0, 8);
  };

  onMount(async () => {
    try {
      const [r, c, s, conns] = await Promise.all([
        api.get<RouteConfig[]>('/apimanagement/routes'),
        api.get<ClusterConfig[]>('/apimanagement/clusters'),
        api.getTransformSchemas(),
        api.getConnections(),
      ]);
      setRoutes(r);
      setClusters(c);
      setSchemas(s);
      setConnections(conns);
    } catch (err) {
      addToast('error', '加载路由、集群或 Schema 失败');
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
      return r.name.includes(search()) || (r.match?.path || '').includes(search()) || (r.match?.host || '').includes(search());
    }
    return true;
  });

  const handleSave = async (route: RouteConfig) => {
    try {
      const res: RouteSaveResponse = route.id
        ? await api.put(`/apimanagement/routes/${route.id}`, route)
        : await api.post('/apimanagement/routes', route);

      const publish = res.publish;
      if (publish) {
        if (publish.conflicts.length > 0) {
          addToast('warning', `路由已保存，但发布存在冲突：${publish.conflicts.join('；')}`);
        } else if (publish.dnsCreated.length > 0) {
          addToast('success', `路由已保存并发布：新增 CNAME ${publish.dnsCreated.join(', ')}`);
        } else {
          addToast('success', '路由已保存，发布状态已同步');
        }
      } else {
        addToast('success', route.id ? '路由已更新' : '路由已创建');
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

  const columns: Column<RouteConfig>[] = [
    {
      key: 'name',
      title: '路由名称',
      mobileVisible: true,
      cell: (route) => <span class="font-medium">{route.name}</span>,
    },
    {
      key: 'path',
      title: '路径',
      cell: (route) => <span class="font-mono text-sm">{route.match?.path || '-'}</span>,
    },
    {
      key: 'host',
      title: '匹配域名',
      cell: (route) => <span class="font-mono text-sm">{route.match?.host || '-'}</span>,
    },
    {
      key: 'clusterId',
      title: '集群',
      cell: (route) => route.clusterId,
    },
    {
      key: 'connectionId',
      title: '接入通道',
      cell: (route) => (
        route.connectionId && route.connectionId !== 'local'
          ? (
            <div class="flex flex-col items-start gap-0.5">
              <span class="badge badge-warning">{connectionName(route.connectionId)}</span>
              <span class="text-xs text-secondary">经数据面转发</span>
            </div>
          )
          : <span class="badge badge-success">本地</span>
      ),
    },
    {
      key: 'enabled',
      title: '状态',
      mobileVisible: true,
      cell: (route) => (
        <span class={`badge ${route.enabled ? 'badge-success' : 'badge-warning'}`}>
          {route.enabled ? '启用' : '禁用'}
        </span>
      ),
    },
    {
      key: 'actions',
      title: '操作',
      mobileVisible: true,
      mobileDetailVisible: false,
      class: 'w-20',
      cell: (route) => (
        <div class="flex space-x-2">
          <button
            class="btn-text btn-text-primary"
            onClick={(e) => { e.stopPropagation(); setEditingRoute(route); setShowModal(true); }}
          >
            编辑
          </button>
          <button
            class="btn-text btn-text-danger"
            onClick={(e) => { e.stopPropagation(); handleDelete(route.id); }}
          >
            删除
          </button>
        </div>
      ),
    },
  ];

  return (
    <div>
      <div class="mb-6 flex items-center justify-between">
        <div>
          <h2 class="text-2xl font-bold mb-2">路由管理</h2>
          <p class="text-secondary">配置和管理API路由规则</p>
        </div>
        <button class="btn btn-primary" onClick={() => { setEditingRoute(null); setShowModal(true); }}>
          <FaSolidPlus class="mr-2" />添加路由
        </button>
      </div>

      <div class="card">
        <div class="flex flex-col md:flex-row md:items-center md:justify-between mb-4 gap-4">
          <div class="relative flex-1 max-w-md">
            <input type="text" placeholder="搜索路由..." class="input pl-10"
              value={search()} onInput={(e) => setSearch(e.currentTarget.value)} />
            <FaSolidSearch class="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" />
          </div>
          <select class="input w-auto" value={filterEnabled()} onChange={(e) => setFilterEnabled(e.currentTarget.value)}>
            <option value="all">所有状态</option>
            <option value="enabled">启用</option>
            <option value="disabled">禁用</option>
          </select>
        </div>

        <Table
          columns={columns}
          data={filteredRoutes()}
          rowKey={(r) => r.id}
          emptyMessage={
            loading() ? '加载中...' : '暂无路由'
          }
        />
      </div>

      {showModal() && (
        <RouteFormModal
          route={editingRoute()}
          routes={routes()}
          clusters={clusters()}
          schemas={schemas()}
          connections={connections()}
          onSave={handleSave}
          onClose={() => setShowModal(false)}
        />
      )}
    </div>
  );
}

// ─── Helper functions ───

/**
 * 归一化匹配域名：去空白与尾部点、转小写，空值即「不限域名」。
 * 与后端 src/MinGo.Core/Routing/RouteMatchKey.cs 同一口径，改动必须同步，
 * 否则前端预检会放行后端 400 的规则（或反之拦掉合法规则）。
 */
function normalizeHostKey(host?: string): string {
  const value = (host ?? '').trim().replace(/\.+$/, '').toLowerCase();
  return value || '*';
}

/** 归一化匹配路径：转小写、补前导斜杠，空值与 catch-all 写法都视为「任意路径」 */
function normalizePathKey(path?: string): string {
  const value = (path ?? '').trim().toLowerCase();
  if (!value || ['/*', '/**', '/{**catch-all}', '/{**catchall}', '*', '{**catch-all}', '{**catchall}'].includes(value)) {
    return '*';
  }
  return value.startsWith('/') ? value : `/${value}`;
}

/** 一条路由在底层生效的匹配键：域名 + 路径 */
function matchKeyOf(host?: string, path?: string): string {
  return `${normalizeHostKey(host)} ${normalizePathKey(path)}`;
}

/**
 * 匹配路径的轻量语法预检：只拦两类必错写法——无前导斜杠、花括号不配对（如 /{**catch-all) 漏写 }）。
 * 前端无法完整复刻后端的 RoutePatternFactory 解析器，完整校验由保存期 400 兜底。
 */
function pathSyntaxError(path: string): string | null {
  const value = path.trim();
  if (!value) return null;
  if (!value.startsWith('/')) return '匹配路径必须以 / 开头';
  let depth = 0;
  for (const ch of value) {
    if (ch === '{') depth++;
    else if (ch === '}' && --depth < 0) return '匹配路径存在多余的 “}”';
  }
  return depth > 0 ? '匹配路径存在未闭合的 “{”（如 /{**catch-all)）' : null;
}

/** 匹配键的可读描述，用于提示文案 */
function describeMatchKey(host?: string, path?: string): string {
  const h = normalizeHostKey(host);
  const p = normalizePathKey(path);
  return `${h === '*' ? '不限域名' : `域名 “${h}”`} + ${p === '*' ? '任意路径' : `路径 “${p}”`}`;
}

/** 从 transforms 列表中获取匹配某个 schema 的所有 entry */
function entriesForSchema(transforms: Record<string, string>[], schemaTypeKey: string): Record<string, string>[] {
  return transforms.filter(t => schemaTypeKey in t);
}

/** 替换或添加一条 entry，返回新列表。对非列表类型会替换同类型的旧 entry。 */
function upsertTransform(transforms: Record<string, string>[], entry: Record<string, string>, schema: TransformSchema): Record<string, string>[] {
  const key = schema.type;
  // 列表中已有相同标识的 entry → 替换
  const sameKey = transforms.findIndex(t => t[key] === entry[key]);
  if (sameKey >= 0) {
    const copy = [...transforms];
    copy[sameKey] = entry;
    return copy;
  }
  // 非列表类型：同一个 schema 只能有一条 → 替换已有的
  if (!schema.isList) {
    const oldIdx = transforms.findIndex(t => key in t);
    if (oldIdx >= 0) {
      const copy = [...transforms];
      copy[oldIdx] = entry;
      return copy;
    }
  }
  return [...transforms, entry];
}

/** 从 transforms 中删除一条 entry */
function removeEntry(transforms: Record<string, string>[], entry: Record<string, string>, schema: TransformSchema): Record<string, string>[] {
  const key = schema.type;
  return transforms.filter(t => !(t[key] === entry[key] && key in t));
}

/** 生成 transform 条目的友好摘要（用于卡片展示） */
function entrySummary(schema: TransformSchema, entry: Record<string, string>): string {
  switch (schema.type) {
    case 'PathPrefix': {
      const parts: string[] = [];
      if (entry.PathPrefix) parts.push(`添加 ${entry.PathPrefix}`);
      if (entry.Prefix) parts.push(`移除 ${entry.Prefix}`);
      return parts.join('  ');
    }
    case 'PathPattern':
      return entry.PathPattern || '';
    case 'RequestHeader':
    case 'ResponseHeader':
      return `${entry[schema.type] || '?'}: ${entry.value || ''} (${entry.action || 'Set'})`;
    case 'XForwarded':
      return `${entry.XForwarded || '?'} → ${entry.action || 'Set'}`;
    default:
      return schema.type;
  }
}

const CATEGORY_LABELS: Record<string, string> = {
  path: '路径',
  requestHeader: '请求头',
  responseHeader: '响应头',
  xForwarded: 'X-Forwarded',
};

// ─── RouteFormModal ───

function RouteFormModal(props: {
  route: RouteConfig | null;
  routes: RouteConfig[];
  clusters: ClusterConfig[];
  schemas: TransformSchema[];
  connections: ConnectionSummary[];
  onSave: (r: RouteConfig) => void;
  onClose: () => void;
}) {
  const [name, setName] = createSignal(props.route?.name || '');
  const [path, setPath] = createSignal(props.route?.match?.path || '');
  const [host, setHost] = createSignal(props.route?.match?.host || '');
  const [clusterId, setClusterId] = createSignal(props.route?.clusterId || '');
  const [connectionId, setConnectionId] = createSignal(props.route?.connectionId || 'local');
  const [enabled, setEnabled] = createSignal(props.route?.enabled ?? true);
  const [errors, setErrors] = createSignal<Record<string, string>>({});
  const [showTransforms, setShowTransforms] = createSignal(false);

  const tunnelConnections = () => props.connections.filter((c) => c.kind === 'cloudflare-tunnel');
  const isTunnel = () => connectionId() !== 'local';
  const tunnelName = () =>
    props.connections.find((c) => c.id === connectionId())?.name || '外部隧道';

  // 后端 RouteUniquenessPolicy 的前端预检：同一「域名 + 路径」只允许一条启用路由。
  // 命中就当场拦下，省一次 400 往返；停用路由不占键（它不进底层配置），所以只跟启用路由比。
  // 后端的 400 仍然是最终闸门——这里只是把同样的口径提前说一遍。
  const conflict = createMemo(() => {
    const key = matchKeyOf(host(), path());
    return props.routes.find((r) =>
      r.id !== props.route?.id &&
      r.enabled &&
      matchKeyOf(r.match?.host, r.match?.path) === key,
    );
  });

  // Transforms 状态
  const [transforms, setTransforms] = createSignal<Record<string, string>[]>(props.route?.transforms ?? []);
  // 三步状态机: 'list' | 'pick' | 'configure'
  const [step, setStep] = createSignal<'list' | 'pick' | 'configure'>('list');
  const [pickSchema, setPickSchema] = createSignal<TransformSchema | null>(null);
  const [draftEntry, setDraftEntry] = createSignal<Record<string, string> | null>(null);

  const validate = (): boolean => {
    const errs: Record<string, string> = {};
    if (!name().trim()) errs.name = '路由名称不能为空';
    if (!path().trim()) errs.path = '匹配路径不能为空';
    else {
      const syntax = pathSyntaxError(path());
      if (syntax) errs.path = syntax;
    }
    if (isTunnel() && !host().trim()) errs.host = '发布到隧道的路由必须配置匹配域名';
    // 停用路由不占匹配键，与后端 RouteUniquenessPolicy 保持一致
    if (!errs.host && enabled() && conflict()) {
      errs.host =
        `${describeMatchKey(host(), path())} 已被启用路由 “${conflict()!.name}” 占用，` +
        '同一域名和路径只能有一条启用路由';
    }
    if (!clusterId()) errs.clusterId = '请选择目标集群';
    setErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const handleSubmit = (e: Event) => {
    e.preventDefault();
    if (!validate()) return;
    const t = transforms();
    props.onSave({
      id: props.route?.id || '',
      name: name(),
      clusterId: clusterId(),
      connectionId: connectionId(),
      match: { path: path(), host: host() || undefined },
      enabled: enabled(),
      transforms: t.length > 0 ? t : null,
    });
  };

  // ── 三步状态流转 ──

  /** 开始添加 → 进入 pick 状态 */
  function startAdd() {
    setStep('pick');
    setPickSchema(null);
    setDraftEntry(null);
  }

  /** 在 pick 状态选择一个 transform 类型 → 进入 configure 状态 */
  function pickType(schema: TransformSchema) {
    // 如果是编辑已有 entry，用已有的值作为草稿
    const existing = entriesForSchema(transforms(), schema.type);
    if (existing.length === 1 && !schema.isList) {
      setDraftEntry({ ...existing[0] });
    } else {
      const entry: Record<string, string> = { [schema.type]: '' };
      for (const f of schema.fields) {
        if (f.defaultValue && !entry[f.key]) entry[f.key] = f.defaultValue;
      }
      setDraftEntry(entry);
    }
    setPickSchema(schema);
    setStep('configure');
  }

  /** 点击已有卡片 → 进入 configure 状态编辑 */
  function editEntry(schema: TransformSchema, entry: Record<string, string>) {
    setPickSchema(schema);
    setDraftEntry({ ...entry });
    setStep('configure');
  }

  /** 在 configure 状态修改字段 */
  function updateDraft(fieldKey: string, value: string) {
    const current = draftEntry();
    if (!current) return;
    setDraftEntry({ ...current, [fieldKey]: value });
  }

  /** 确认添加/修改 → 回 list 状态 */
  function confirmEntry() {
    const schema = pickSchema();
    const entry = draftEntry();
    if (!schema || !entry) return;
    setTransforms(upsertTransform(transforms(), entry, schema));
    setStep('list');
    setPickSchema(null);
    setDraftEntry(null);
  }

  /** 取消配置 → 回 list 状态 */
  function cancelEntry() {
    setStep('list');
    setPickSchema(null);
    setDraftEntry(null);
  }

  /** 删除一条 entry */
  function handleRemove(schemaTypeKey: string, entry: Record<string, string>) {
    setTransforms(transforms().filter(t => !(t[schemaTypeKey] === entry[schemaTypeKey] && schemaTypeKey in t)));
  }

  // ── 渲染辅助 ──

  /** 查找 entry 对应的 schema */
  function schemaForEntry(entry: Record<string, string>): TransformSchema | undefined {
    return props.schemas.find(s => s.type in entry);
  }

  const pickSchemas = createMemo(() => {
    const groups = new Map<string, TransformSchema[]>();
    const sorted = [...props.schemas].sort((a, b) => a.order - b.order);
    for (const s of sorted) {
      const list = groups.get(s.category) || [];
      list.push(s);
      groups.set(s.category, list);
    }
    return groups;
  });

  return (
    <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={props.onClose}>
      <div class="card w-full max-w-4xl mx-4 max-h-[90vh] overflow-y-auto relative" onClick={(e) => e.stopPropagation()}>

        {/* ── 内层弹窗（PICK + CONFIGURE，覆盖整个 Modal Card） ── */}
        <Show when={step() === 'pick' || step() === 'configure'}>
          <div class="absolute inset-0 bg-black/30 rounded-lg z-10" onClick={cancelEntry} />
          <div class="absolute inset-0 z-20 flex items-start justify-center overflow-y-auto py-8">
            <div class="bg-white dark:bg-dark-100 rounded-lg shadow-xl border border-gray-200 dark:border-dark-200 w-[85%] max-w-lg" onClick={(e) => e.stopPropagation()}>
              <Show when={step() === 'pick'}>
                <PickView schemasByCategory={pickSchemas()} onPick={pickType} onCancel={cancelEntry} />
              </Show>
              <Show when={step() === 'configure'}>
                <ConfigureView schema={pickSchema()} draft={draftEntry()} onFieldChange={updateDraft} onConfirm={confirmEntry} onCancel={cancelEntry} />
              </Show>
            </div>
          </div>
        </Show>

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
            <label class="block text-sm font-medium mb-1">匹配域名</label>
            <input class="input font-mono text-sm" value={host()} onInput={(e) => setHost(e.currentTarget.value)} placeholder="例如：api.example.com" />
            {errors().host && <p class="text-danger text-xs mt-1">{errors().host}</p>}
            <Show when={enabled() && conflict()}>
              <p class="text-amber-600 dark:text-amber-400 text-xs mt-1">
                与启用路由 “{conflict()!.name}” 的域名和路径重复，保存会被拒绝
              </p>
            </Show>
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">接入通道</label>
            <select
              class="input"
              value={connectionId()}
              onChange={(e) => setConnectionId(e.currentTarget.value)}
            >
              <option value="local">本地数据面（data-plane）</option>
              <For each={tunnelConnections()}>{(conn) => (
                <option value={conn.id}>Cloudflare 隧道：{conn.name}</option>
              )}</For>
            </select>
            <Show when={tunnelConnections().length === 0}>
              <p class="text-secondary text-xs mt-1">未发现可用隧道，请先在「接入通道」页面创建</p>
            </Show>
            <Show when={isTunnel()}>
              <div class="mt-2 p-2.5 rounded-lg bg-blue-50 dark:bg-dark-200 border border-blue-200 dark:border-dark-300 text-xs space-y-1">
                <p class="font-medium">选择外部隧道后，保存时自动生成两条底层规则</p>
                <ol class="list-decimal list-inside space-y-0.5 text-secondary">
                  <li>
                    隧道 ingress：<span class="font-mono">{normalizeHostKey(host())}</span> → 网关回源地址
                    （隧道不直连后端服务）
                  </li>
                  <li>
                    数据面规则：<span class="font-mono">{normalizeHostKey(host())}</span>
                    {' '}<span class="font-mono">{normalizePathKey(path())}</span> → 集群{' '}
                    <span class="font-mono">{clusterId() || '待选择'}</span>
                  </li>
                </ol>
                <p class="text-secondary">
                  外部隧道的流量一律经 {tunnelName()} → 本地数据面 → 后端服务，
                  {tunnelName()} 侧只会看到回源到数据面的那条规则
                </p>
              </div>
            </Show>
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
                <option value={c.id}>{c.id}</option>
              )}</For>
            </select>
            {errors().clusterId && <p class="text-danger text-xs mt-1">{errors().clusterId}</p>}
          </div>
          <div class="flex items-center space-x-2">
            <input type="checkbox" id="enabled" checked={enabled()} onChange={(e) => setEnabled(e.currentTarget.checked)} />
            <label for="enabled">启用</label>
          </div>

          {/* ── Transforms 折叠面板 ── */}
          <div class="border border-gray-200 dark:border-dark-200 rounded-lg overflow-hidden">
            <button
              type="button"
              class="w-full flex items-center justify-between px-4 py-3 text-sm font-medium hover:bg-gray-50 dark:hover:bg-dark-100/50 transition-colors"
              onClick={() => setShowTransforms(!showTransforms())}
            >
              <span>请求变换 (Request Transforms)</span>
              <div class="flex items-center gap-2">
                <span class="text-xs text-secondary">{transforms().length} 项</span>
                {showTransforms() ? <FaSolidChevronDown class="text-gray-400" /> : <FaSolidChevronRight class="text-gray-400" />}
              </div>
            </button>

            <Show when={showTransforms()}>
              <div class="px-4 pb-4">
                <Show when={step() === 'list'}>
                  <ListView
                    transforms={transforms()}
                    schemas={props.schemas}
                    schemaForEntry={schemaForEntry}
                    onEdit={editEntry}
                    onRemove={handleRemove}
                    onAdd={startAdd}
                  />
                </Show>
              </div>
            </Show>
          </div>

          <div class="flex justify-end space-x-2 pt-2">
            <button type="button" class="btn btn-secondary" onClick={props.onClose}>取消</button>
            <button type="submit" class="btn btn-primary">保存</button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ─── 三步视图组件 ───

/** LIST 状态：展示已配置的 transforms 列表 */
function ListView(props: {
  transforms: Record<string, string>[];
  schemas: TransformSchema[];
  schemaForEntry: (entry: Record<string, string>) => TransformSchema | undefined;
  onEdit: (schema: TransformSchema, entry: Record<string, string>) => void;
  onRemove: (schemaTypeKey: string, entry: Record<string, string>) => void;
  onAdd: () => void;
}) {
  return (
    <div class="space-y-2 pt-3">
      <div class="flex items-center justify-between">
        <span class="text-xs text-secondary">已配置 {props.transforms.length} 项变换</span>
        <button type="button" class="btn btn-primary btn-sm flex items-center gap-1" onClick={props.onAdd}>
          <FaSolidPlus class="w-3 h-3" />添加变换
        </button>
      </div>

      <Show when={props.transforms.length === 0}>
        <p class="text-xs text-secondary text-center py-6">暂无变换配置，点击"添加变换"开始配置</p>
      </Show>

      <div class="space-y-1.5">
        <For each={props.transforms}>{(entry) => {
          const schema = props.schemaForEntry(entry);
          if (!schema) {
            return (
              <div class="flex items-center justify-between bg-gray-50 dark:bg-dark-100/30 rounded px-3 py-2 text-xs text-secondary font-mono">
                <span>{JSON.stringify(entry)}</span>
                <button type="button" class="text-danger hover:text-red-700" onClick={() => props.onRemove(Object.keys(entry)[0], entry)}>
                  <FaSolidTrash class="w-3 h-3" />
                </button>
              </div>
            );
          }
          return (
            <div class="flex items-center justify-between bg-gray-50 dark:bg-dark-100/30 rounded px-3 py-2 hover:bg-gray-100 dark:hover:bg-dark-100/50 transition-colors cursor-pointer group"
              onClick={() => props.onEdit(schema, entry)}
            >
              <div class="flex items-center gap-3 min-w-0">
                <span class="text-xs font-medium text-primary shrink-0 w-20">{schema.displayName}</span>
                <span class="text-xs text-secondary truncate">{entrySummary(schema, entry)}</span>
              </div>
              <button type="button" class="text-gray-300 group-hover:text-danger transition-colors shrink-0"
                onClick={(e) => { e.stopPropagation(); props.onRemove(schema.type, entry); }}>
                <FaSolidTrash class="w-3 h-3" />
              </button>
            </div>
          );
        }}</For>
      </div>
    </div>
  );
}

/** PICK 状态：选择要添加的 transform 类型 */
function PickView(props: {
  schemasByCategory: Map<string, TransformSchema[]>;
  onPick: (schema: TransformSchema) => void;
  onCancel: () => void;
}) {
  return (
    <div class="space-y-3">
      <div class="flex items-center justify-between px-4 pt-4 pb-2 border-b border-gray-100 dark:border-dark-100">
        <span class="text-sm font-medium">选择变换方式</span>
        <button type="button" class="text-xs text-secondary hover:text-primary transition-colors" onClick={props.onCancel}>取消</button>
      </div>
      <div class="px-4 pb-4 space-y-3 max-h-[55vh] overflow-y-auto">
        <For each={Array.from(props.schemasByCategory.entries())}>{(entry) => {
          const [category, schemas] = entry;
          return (
            <div>
              <h5 class="text-xs text-secondary mb-1.5">{CATEGORY_LABELS[category] || category}</h5>
              <div class="grid grid-cols-2 gap-1.5">
                <For each={schemas}>{(schema) => (
                  <button
                    type="button"
                    class="text-left border border-gray-200 dark:border-dark-200 rounded-lg px-3 py-2.5 hover:border-primary hover:bg-primary/10 bg-white dark:bg-dark-100 transition-colors"
                    onClick={() => props.onPick(schema)}
                  >
                    <div class="text-xs font-medium">{schema.displayName}</div>
                    <div class="text-xs text-secondary mt-0.5 truncate">{schema.description}</div>
                  </button>
                )}</For>
              </div>
            </div>
          );
        }}</For>
      </div>
    </div>
  );
}

/** CONFIGURE 状态：配置选中的 transform（内层弹窗内容） */
function ConfigureView(props: {
  schema: TransformSchema | null;
  draft: Record<string, string> | null;
  onFieldChange: (fieldKey: string, value: string) => void;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <div class="space-y-3">
      <div class="flex items-center justify-between px-4 pt-4 pb-2 border-b border-gray-100 dark:border-dark-100">
        <span class="text-sm font-medium">{props.schema?.displayName || '配置变换'}</span>
        <button type="button" class="text-xs text-secondary hover:text-primary transition-colors" onClick={props.onCancel}>取消</button>
      </div>

      <div class="px-4 pb-4 space-y-3">
        <Show when={props.draft && props.schema}>
          <div class="space-y-2">
            <For each={props.schema!.fields}>{(field) => (
              <div>
                <label class="block text-xs text-secondary mb-0.5">{field.label}</label>
                <Show when={field.type === 'select'} fallback={
                  <input class="input text-sm" type="text" value={props.draft![field.key] ?? ''}
                    placeholder={field.placeholder}
                    onInput={(e) => props.onFieldChange(field.key, e.currentTarget.value)} />
                }>
                  <select class="input text-sm" value={props.draft![field.key] ?? ''}
                    onChange={(e) => props.onFieldChange(field.key, e.currentTarget.value)}>
                    <For each={field.options}>{(opt) => (
                      <option value={opt}>{opt}</option>
                    )}</For>
                  </select>
                </Show>
              </div>
            )}</For>
          </div>
        </Show>

        <div class="flex justify-end gap-2 pt-1">
          <button type="button" class="btn btn-secondary btn-sm" onClick={props.onCancel}>取消</button>
          <button type="button" class="btn btn-primary btn-sm" onClick={props.onConfirm}>确认</button>
        </div>
      </div>
    </div>
  );
}
