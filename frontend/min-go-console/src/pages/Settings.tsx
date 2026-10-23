import { createSignal, onMount, Show } from 'solid-js';
import { addToast } from '../store/toast';
import { api } from '../api/client';
import type { PortainerStatus } from '../types';
import { FaSolidUndo, FaSolidSave } from 'solid-icons/fa';

const DEFAULT_GATEWAY_NAME = 'MinGo API Gateway';

export default function SettingsPage() {
  const [gatewayName, setGatewayName] = createSignal(DEFAULT_GATEWAY_NAME);
  const [logLevel, setLogLevel] = createSignal('INFO');
  const [healthInterval, setHealthInterval] = createSignal('10');
  const [timeout, setTimeout_] = createSignal('30');

  const [portainerStatus, setPortainerStatus] = createSignal<PortainerStatus | null>(null);
  const [portainerLoading, setPortainerLoading] = createSignal(true);
  const [portainerError, setPortainerError] = createSignal<string | null>(null);

  const loadPortainerStatus = async () => {
    setPortainerLoading(true);
    setPortainerError(null);
    setPortainerStatus(null);
    try {
      setPortainerStatus(await api.getPortainerStatus());
    } catch (err) {
      setPortainerError((err as Error).message || '获取 Portainer 状态失败');
    } finally {
      setPortainerLoading(false);
    }
  };

  onMount(loadPortainerStatus);

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

      <div class="card mt-6">
        <div class="flex items-start justify-between gap-4 mb-4">
          <div>
            <h3 class="font-semibold">Portainer 集成</h3>
            <p class="text-sm text-secondary mt-1">
              只读发现容器并生成集群目标；凭据仅从配置或环境变量读取，控制台不存储、不回显。
            </p>
          </div>
          <button
            class="btn btn-secondary shrink-0"
            onClick={loadPortainerStatus}
            disabled={portainerLoading()}
          >
            {portainerLoading() ? '检测中...' : '测试连接'}
          </button>
        </div>

        <Show when={portainerLoading()}>
          <div class="text-sm text-secondary py-6 text-center">正在检测 Portainer...</div>
        </Show>

        <Show when={!portainerLoading() && portainerError()}>
          <div class="bg-danger/10 border border-danger/30 text-danger rounded-lg px-4 py-3 text-sm break-all">
            {portainerError()}
          </div>
        </Show>

        <Show when={!portainerLoading() && !portainerError() && portainerStatus()}>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-3 text-sm">
            <div class="p-3 rounded-lg bg-gray-50 dark:bg-dark-200 md:col-span-2">
              <p class="text-xs text-secondary mb-1">Base URL</p>
              <span class="font-mono text-xs break-all">
                {portainerStatus()!.baseUrl || '-'}
              </span>
            </div>
            <div class="p-3 rounded-lg bg-gray-50 dark:bg-dark-200">
              <p class="text-xs text-secondary mb-1">启用状态</p>
              <span class={`badge ${portainerStatus()!.enabled ? 'badge-success' : 'badge-warning'}`}>
                {portainerStatus()!.enabled ? '已启用' : '未启用'}
              </span>
            </div>
            <div class="p-3 rounded-lg bg-gray-50 dark:bg-dark-200">
              <p class="text-xs text-secondary mb-1">连接</p>
              <Show
                when={portainerStatus()!.enabled}
                fallback={<span class="text-secondary">-</span>}
              >
                <span class={`badge ${portainerStatus()!.reachable ? 'badge-success' : 'badge-danger'}`}>
                  {portainerStatus()!.reachable ? '可达' : '不可达'}
                </span>
              </Show>
            </div>
            <div class="p-3 rounded-lg bg-gray-50 dark:bg-dark-200">
              <p class="text-xs text-secondary mb-1">Portainer 版本</p>
              <span class="font-mono">{portainerStatus()!.version || '-'}</span>
            </div>
            <div class="p-3 rounded-lg bg-gray-50 dark:bg-dark-200">
              <p class="text-xs text-secondary mb-1">Endpoint 数量</p>
              <span>{portainerStatus()!.endpointCount ?? '-'}</span>
            </div>
            <div class="p-3 rounded-lg bg-gray-50 dark:bg-dark-200">
              <p class="text-xs text-secondary mb-1">API Key</p>
              <span class={`badge ${portainerStatus()!.apiKeyConfigured ? 'badge-success' : 'badge-warning'}`}>
                {portainerStatus()!.apiKeyConfigured ? '已配置' : '未配置'}
              </span>
            </div>
            <div class="p-3 rounded-lg bg-gray-50 dark:bg-dark-200">
              <p class="text-xs text-secondary mb-1">自身 stack 识别</p>
              <Show
                when={portainerStatus()!.selfDetection?.detected}
                fallback={<span class="text-warning text-xs">未识别（可配置 SelfStackNames）</span>}
              >
                <span class="font-mono text-xs">
                  {portainerStatus()!.selfDetection.stacks.join(', ')}
                </span>
                <span class="text-xs text-secondary ml-2">
                  {selfSourceLabel(portainerStatus()!.selfDetection.source)} ·{' '}
                  {portainerStatus()!.selfDetection.containerCount} 个容器
                </span>
              </Show>
            </div>
          </div>

          <Show when={portainerStatus()!.error}>
            <p class="text-danger text-xs mt-3 break-all">
              最近错误：{portainerStatus()!.error}
            </p>
          </Show>
        </Show>

        <p class="text-xs text-secondary mt-4 pt-4 border-t border-gray-200 dark:border-dark-200">
          配置键：Portainer:Enabled、Portainer:BaseUrl、Portainer:ApiKey、Portainer:HostAddress；容器部署可使用环境变量
          Portainer__Enabled / Portainer__BaseUrl / Portainer__ApiKey 覆盖。修改配置后需重启控制面。
        </p>
      </div>
    </div>
  );
}

function selfSourceLabel(source: string): string {
  switch (source) {
    case 'config':
      return '配置覆盖';
    case 'hostname':
      return '主机名自动识别';
    case 'image':
      return '镜像兜底识别';
    default:
      return '未识别';
  }
}
