import { createSignal, onMount, Show, For, Index } from 'solid-js';
import { addToast } from '../store/toast';
import { api } from '../api/client';
import type { PortainerInstance, PortainerStatus } from '../types';
import {
  FaSolidUndo,
  FaSolidSave,
  FaSolidPlus,
  FaSolidEdit,
  FaSolidTrash,
  FaSolidTimes,
  FaSolidCheck,
  FaSolidSpinner,
} from 'solid-icons/fa';

const DEFAULT_GATEWAY_NAME = 'MinGo API Gateway';

export default function SettingsPage() {
  const [gatewayName, setGatewayName] = createSignal(DEFAULT_GATEWAY_NAME);
  const [logLevel, setLogLevel] = createSignal('INFO');
  const [healthInterval, setHealthInterval] = createSignal('10');
  const [timeout, setTimeout_] = createSignal('30');

  // Portainer 实例管理
  const [instances, setInstances] = createSignal<PortainerInstance[]>([]);
  const [instancesLoading, setInstancesLoading] = createSignal(true);
  const [showForm, setShowForm] = createSignal(false);
  const [editingInstance, setEditingInstance] = createSignal<PortainerInstance | null>(null);
  const [testingId, setTestingId] = createSignal<string | null>(null);
  const [testResult, setTestResult] = createSignal<PortainerStatus | null>(null);

  const loadInstances = async () => {
    setInstancesLoading(true);
    try {
      setInstances(await api.getPortainerInstances());
    } catch (err) {
      addToast('error', `加载 Portainer 实例列表失败: ${(err as Error).message}`);
    } finally {
      setInstancesLoading(false);
    }
  };

  onMount(loadInstances);

  const handleSave = () => {
    addToast('warning', '设置保存功能尚未开放，敬请期待');
  };

  const handleReset = () => {
    setGatewayName(DEFAULT_GATEWAY_NAME);
    setLogLevel('INFO');
    setHealthInterval('10');
    setTimeout_('30');
    addToast('success', '已恢复默认设置');
  };

  const handleDelete = async (id: string) => {
    if (!confirm(`确定删除 Portainer 实例 "${id}"？`)) return;
    try {
      await api.deletePortainerInstance(id);
      addToast('success', `已删除 Portainer 实例 "${id}"`);
      await loadInstances();
    } catch (err) {
      addToast('error', `删除失败: ${(err as Error).message}`);
    }
  };

  const handleTest = async (id: string) => {
    setTestingId(id);
    setTestResult(null);
    try {
      const status = await api.testPortainerInstance(id);
      setTestResult(status);
      if (status.reachable) {
        addToast('success', `实例 "${id}" 连接成功 (Portainer v${status.version})`);
      } else {
        addToast('error', `实例 "${id}" 连接失败: ${status.error || '未知错误'}`);
      }
    } catch (err) {
      addToast('error', `测试连接失败: ${(err as Error).message}`);
    } finally {
      setTestingId(null);
    }
  };

  const handleFormSave = async (instance: PortainerInstance) => {
    try {
      if (editingInstance()) {
        await api.updatePortainerInstance(instance.id, instance);
        addToast('success', `已更新 Portainer 实例 "${instance.id}"`);
      } else {
        await api.createPortainerInstance(instance);
        addToast('success', `已创建 Portainer 实例 "${instance.id}"`);
      }
      setShowForm(false);
      setEditingInstance(null);
      await loadInstances();
    } catch (err) {
      addToast('error', `保存失败: ${(err as Error).message}`);
    }
  };

  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">设置</h2>
        <p class="text-secondary">配置网关全局设置</p>
      </div>

      <div class="card">
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <div>
            <h3 class="font-semibold mb-4">基本设置</h3>
            <div class="space-y-4">
              <div>
                <label class="block text-sm font-medium mb-1">网关名称</label>
                <input type="text" class="input" value={gatewayName()} onInput={(e) => setGatewayName(e.currentTarget.value)} />
              </div>
              <div>
                <label class="block text-sm font-medium mb-1">日志级别</label>
                <select class="input" value={logLevel()} onChange={(e) => setLogLevel(e.currentTarget.value)}>
                  <option value="INFO">INFO</option>
                  <option value="DEBUG">DEBUG</option>
                  <option value="WARN">WARN</option>
                  <option value="ERROR">ERROR</option>
                </select>
              </div>
            </div>
          </div>
          <div>
            <h3 class="font-semibold mb-4">高级设置</h3>
            <div class="space-y-4">
              <div>
                <label class="block text-sm font-medium mb-1">健康检查间隔</label>
                <div class="flex items-center space-x-2">
                  <input type="number" class="input" value={healthInterval()} onInput={(e) => setHealthInterval(e.currentTarget.value)} />
                  <span class="text-secondary">秒</span>
                </div>
              </div>
              <div>
                <label class="block text-sm font-medium mb-1">超时时间</label>
                <div class="flex items-center space-x-2">
                  <input type="number" class="input" value={timeout()} onInput={(e) => setTimeout_(e.currentTarget.value)} />
                  <span class="text-secondary">秒</span>
                </div>
              </div>
            </div>
          </div>
        </div>
        <div class="mt-6 flex justify-end space-x-2">
          <button class="btn btn-secondary" onClick={handleReset}><FaSolidUndo class="mr-2" />重置</button>
          <button class="btn btn-primary" onClick={handleSave}><FaSolidSave class="mr-2" />保存设置</button>
        </div>
      </div>

      {/* Portainer 实例管理 */}
      <div class="card mt-6">
        <div class="flex items-center justify-between gap-4 mb-4">
          <div>
            <h3 class="font-semibold">Portainer 实例</h3>
            <p class="text-sm text-secondary mt-1">
              管理多个 Portainer 服务器实例，用于只读发现和导入容器。
            </p>
          </div>
          <button
            class="btn btn-primary shrink-0"
            onClick={() => {
              setEditingInstance(null);
              setShowForm(true);
            }}
          >
            <FaSolidPlus class="mr-2" />添加实例
          </button>
        </div>

        <Show when={instancesLoading()}>
          <div class="text-sm text-secondary py-6 text-center">正在加载...</div>
        </Show>

        <Show when={!instancesLoading() && instances().length === 0}>
          <div class="text-sm text-secondary py-6 text-center border border-dashed border-gray-200 dark:border-dark-200 rounded-lg">
            尚未添加任何 Portainer 实例
          </div>
        </Show>

        <Show when={!instancesLoading() && instances().length > 0}>
          <div class="overflow-x-auto">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b border-gray-200 dark:border-dark-200 text-left">
                  <th class="py-2 px-3 font-medium text-secondary">ID</th>
                  <th class="py-2 px-3 font-medium text-secondary">名称</th>
                  <th class="py-2 px-3 font-medium text-secondary">地址</th>
                  <th class="py-2 px-3 font-medium text-secondary">状态</th>
                  <th class="py-2 px-3 font-medium text-secondary">API Key</th>
                  <th class="py-2 px-3 font-medium text-secondary text-right">操作</th>
                </tr>
              </thead>
              <tbody>
                <For each={instances()}>
                  {(inst) => (
                    <tr class="border-b border-gray-100 dark:border-dark-200 hover:bg-gray-50 dark:hover:bg-dark-200 transition">
                      <td class="py-2 px-3 font-mono text-xs">{inst.id}</td>
                      <td class="py-2 px-3">{inst.name || '-'}</td>
                      <td class="py-2 px-3 font-mono text-xs break-all">{inst.baseUrl || '-'}</td>
                      <td class="py-2 px-3">
                        <span class={`badge ${inst.enabled ? 'badge-success' : 'badge-warning'}`}>
                          {inst.enabled ? '启用' : '禁用'}
                        </span>
                      </td>
                      <td class="py-2 px-3 font-mono text-xs">
                        {inst.apiKey && inst.apiKey.length > 0 ? (
                          <span class="badge badge-success text-xs">已配置</span>
                        ) : (
                          <span class="badge badge-warning text-xs">未配置</span>
                        )}
                      </td>
                      <td class="py-2 px-3">
                        <div class="flex items-center justify-end gap-1">
                          <button
                            class="btn-icon btn-icon-secondary"
                            title="测试连接"
                            disabled={testingId() === inst.id}
                            onClick={() => handleTest(inst.id)}
                          >
                            <Show when={testingId() === inst.id} fallback={<FaSolidCheck />}>
                              <FaSolidSpinner class="animate-spin" />
                            </Show>
                          </button>
                          <button
                            class="btn-icon btn-icon-primary"
                            title="编辑"
                            onClick={() => {
                              setEditingInstance(inst);
                              setShowForm(true);
                            }}
                          >
                            <FaSolidEdit />
                          </button>
                          <button
                            class="btn-icon btn-icon-danger"
                            title="删除"
                            onClick={() => handleDelete(inst.id)}
                          >
                            <FaSolidTrash />
                          </button>
                        </div>
                      </td>
                    </tr>
                  )}
                </For>
              </tbody>
            </table>
          </div>

          <Show when={testResult()}>
            <div class="mt-3 p-3 rounded-lg bg-gray-50 dark:bg-dark-200 text-xs">
              <p>
                <span class="text-secondary">最近测试：</span>
                <span class={testResult()!.reachable ? 'text-success' : 'text-danger'}>
                  {testResult()!.reachable ? '连接成功' : '连接失败'}
                </span>
                <Show when={testResult()!.version}>
                  <span class="ml-2 text-secondary">v{testResult()!.version}</span>
                </Show>
                <Show when={testResult()!.endpointCount !== null && testResult()!.endpointCount !== undefined}>
                  <span class="ml-2 text-secondary">{testResult()!.endpointCount} 个 endpoint</span>
                </Show>
              </p>
              <Show when={testResult()!.error}>
                <p class="text-danger mt-1 break-all">{testResult()!.error}</p>
              </Show>
            </div>
          </Show>
        </Show>
      </div>

      {/* 实例表单 Modal */}
      <Show when={showForm()}>
        <InstanceFormModal
          instance={editingInstance()}
          onSave={handleFormSave}
          onClose={() => {
            setShowForm(false);
            setEditingInstance(null);
          }}
        />
      </Show>
    </div>
  );
}

function InstanceFormModal(props: {
  instance: PortainerInstance | null;
  onSave: (inst: PortainerInstance) => void;
  onClose: () => void;
}) {
  const isEdit = () => props.instance !== null;

  const [id, setId] = createSignal(props.instance?.id || '');
  const [name, setName] = createSignal(props.instance?.name || '');
  const [enabled, setEnabled] = createSignal(props.instance?.enabled ?? true);
  const [baseUrl, setBaseUrl] = createSignal(props.instance?.baseUrl || '');
  const [apiKey, setApiKey] = createSignal(props.instance?.apiKey || '');
  const [hostAddress, setHostAddress] = createSignal(props.instance?.hostAddress || '');
  const [dataPlaneService, setDataPlaneService] = createSignal(props.instance?.dataPlaneService || 'data-plane');
  const [timeoutSeconds, setTimeoutSeconds] = createSignal(props.instance?.timeoutSeconds ?? 10);
  const [cacheSeconds, setCacheSeconds] = createSignal(props.instance?.cacheSeconds ?? 30);
  const [selfStackNames, setSelfStackNames] = createSignal(props.instance?.selfStackNames?.join(', ') || '');
  const [selfImagePatterns, setSelfImagePatterns] = createSignal(props.instance?.selfImagePatterns?.join(', ') || 'reverseproxy-cp');
  const [extraExcludedStacks, setExtraExcludedStacks] = createSignal(props.instance?.extraExcludedStacks?.join(', ') || '');

  const [errors, setErrors] = createSignal<Record<string, string>>({});
  const [submitting, setSubmitting] = createSignal(false);

  const validate = (): boolean => {
    const errs: Record<string, string> = {};
    if (!id().trim()) errs.id = '实例 ID 不能为空';
    if (!baseUrl().trim()) errs.baseUrl = 'Base URL 不能为空';
    else {
      try { new URL(baseUrl().trim()); } catch { errs.baseUrl = 'Base URL 格式不合法'; }
    }
    if (!apiKey().trim() && !isEdit()) errs.apiKey = 'API Key 不能为空';
    setErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    if (!validate()) return;
    setSubmitting(true);

    const instance: PortainerInstance = {
      id: id().trim(),
      name: name().trim(),
      enabled: enabled(),
      baseUrl: baseUrl().trim(),
      apiKey: apiKey().trim(),
      hostAddress: hostAddress().trim() || null,
      dataPlaneService: dataPlaneService().trim() || 'data-plane',
      timeoutSeconds: timeoutSeconds(),
      cacheSeconds: cacheSeconds(),
      selfStackNames: selfStackNames().split(',').map(s => s.trim()).filter(Boolean),
      selfImagePatterns: selfImagePatterns().split(',').map(s => s.trim()).filter(Boolean),
      extraExcludedStacks: extraExcludedStacks().split(',').map(s => s.trim()).filter(Boolean),
      createdAt: props.instance?.createdAt || new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };

    try {
      await props.onSave(instance);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div
      class="fixed inset-0 bg-black/50 flex items-center justify-center z-50"
      onClick={props.onClose}
    >
      <div
        class="card w-full max-w-2xl mx-4 max-h-[90vh] overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        <div class="flex items-center justify-between mb-4">
          <h3 class="font-semibold">{isEdit() ? '编辑 Portainer 实例' : '添加 Portainer 实例'}</h3>
          <button onClick={props.onClose} class="text-secondary hover:text-gray-800 dark:hover:text-gray-200">
            <FaSolidTimes />
          </button>
        </div>

        <form onSubmit={handleSubmit} class="space-y-4">
          <div class="grid grid-cols-2 gap-4">
            <div>
              <label class="block text-sm font-medium mb-1">实例 ID</label>
              <Show when={isEdit()} fallback={
                <input
                  class="input font-mono"
                  value={id()}
                  onInput={(e) => setId(e.currentTarget.value)}
                  placeholder="例如 dc-shanghai"
                  required
                />
              }>
                <p class="input bg-gray-100 dark:bg-dark-200 cursor-not-allowed font-mono">{id()}</p>
              </Show>
              {errors().id && <p class="text-danger text-xs mt-1">{errors().id}</p>}
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">显示名称</label>
              <input
                class="input"
                value={name()}
                onInput={(e) => setName(e.currentTarget.value)}
                placeholder="例如 上海机房 Portainer"
              />
            </div>
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">Base URL</label>
            <input
              class="input font-mono"
              value={baseUrl()}
              onInput={(e) => setBaseUrl(e.currentTarget.value)}
              placeholder="https://portainer.example.com"
              required
            />
            {errors().baseUrl && <p class="text-danger text-xs mt-1">{errors().baseUrl}</p>}
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">API Key</label>
            <input
              class="input font-mono"
              type="password"
              value={apiKey()}
              onInput={(e) => setApiKey(e.currentTarget.value)}
              placeholder={isEdit() ? '留空保持不变，输入新值覆盖' : 'Portainer API Key'}
              required={!isEdit()}
            />
            {errors().apiKey && <p class="text-danger text-xs mt-1">{errors().apiKey}</p>}
            <Show when={isEdit()}>
              <p class="text-xs text-secondary mt-1">当前值已脱敏显示，留空则不修改</p>
            </Show>
          </div>

          <div class="grid grid-cols-2 gap-4">
            <div>
              <label class="block text-sm font-medium mb-1">宿主地址 (HostAddress)</label>
              <input
                class="input font-mono"
                value={hostAddress()}
                onInput={(e) => setHostAddress(e.currentTarget.value)}
                placeholder="可选，例如 10.0.1.1"
              />
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">DataPlane Service</label>
              <input
                class="input font-mono"
                value={dataPlaneService()}
                onInput={(e) => setDataPlaneService(e.currentTarget.value)}
                placeholder="data-plane"
              />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-4">
            <div>
              <label class="block text-sm font-medium mb-1">请求超时 (秒)</label>
              <input
                type="number"
                class="input"
                value={timeoutSeconds()}
                onInput={(e) => setTimeoutSeconds(parseInt(e.currentTarget.value) || 10)}
                min="1"
                max="120"
              />
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">发现缓存 (秒)</label>
              <input
                type="number"
                class="input"
                value={cacheSeconds()}
                onInput={(e) => setCacheSeconds(parseInt(e.currentTarget.value) || 30)}
                min="0"
                max="3600"
              />
            </div>
          </div>

          <div>
            <label class="flex items-center gap-2">
              <input
                type="checkbox"
                checked={enabled()}
                onChange={(e) => setEnabled(e.currentTarget.checked)}
              />
              <span class="text-sm font-medium">启用此实例</span>
            </label>
          </div>

          <div class="border-t border-gray-200 dark:border-dark-200 pt-4">
            <h4 class="text-sm font-semibold mb-3">自身识别与排除</h4>
            <div class="space-y-3">
              <div>
                <label class="block text-sm font-medium mb-1">自身 Stack 名称 (逗号分隔)</label>
                <input
                  class="input font-mono text-sm"
                  value={selfStackNames()}
                  onInput={(e) => setSelfStackNames(e.currentTarget.value)}
                  placeholder="留空自动识别"
                />
              </div>
              <div>
                <label class="block text-sm font-medium mb-1">自身镜像模式 (逗号分隔)</label>
                <input
                  class="input font-mono text-sm"
                  value={selfImagePatterns()}
                  onInput={(e) => setSelfImagePatterns(e.currentTarget.value)}
                  placeholder="reverseproxy-cp"
                />
              </div>
              <div>
                <label class="block text-sm font-medium mb-1">额外排除的 Stack (逗号分隔)</label>
                <input
                  class="input font-mono text-sm"
                  value={extraExcludedStacks()}
                  onInput={(e) => setExtraExcludedStacks(e.currentTarget.value)}
                  placeholder="留空无额外排除"
                />
              </div>
            </div>
          </div>

          <div class="flex justify-end space-x-2 pt-4 border-t border-gray-200 dark:border-dark-200">
            <button type="button" class="btn btn-secondary" onClick={props.onClose}>
              取消
            </button>
            <button type="submit" class="btn btn-primary" disabled={submitting()}>
              {submitting() ? '保存中...' : '保存'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
