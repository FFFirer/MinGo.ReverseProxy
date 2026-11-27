import { createSignal, For, Show } from 'solid-js';
import { FaSolidCheck, FaSolidPlus } from 'solid-icons/fa';
import { api } from '../../api/client';
import { addToast } from '../../store/toast';
import type { DataPlaneNode, GatewayInstance } from '../../types';

interface Props {
  instance: GatewayInstance;
  nodes: DataPlaneNode[];
  onChanged: () => Promise<void> | void;
  onClose: () => void;
}

const DEFAULT_NODE_ID = 'default';

export default function NodeSelectModal(props: Props) {
  const [busy, setBusy] = createSignal(false);
  const [creating, setCreating] = createSignal(false);
  const [newName, setNewName] = createSignal('');
  const [formError, setFormError] = createSignal('');

  const currentId = () => props.instance.nodeId || '';

  const bind = async (nodeId: string | null) => {
    if (busy()) return;
    setBusy(true);
    try {
      await api.setInstanceNode(props.instance.instanceId, nodeId);
      addToast('success', nodeId ? '节点绑定已更新' : '已解除绑定（该实例不再接收配置）');
      await props.onChanged();
      props.onClose();
    } catch (err) {
      addToast('error', `更新节点绑定失败: ${(err as Error).message}`);
    } finally {
      setBusy(false);
    }
  };

  const createAndBind = async (e: Event) => {
    e.preventDefault();
    const name = newName().trim();
    if (!name) {
      setFormError('节点名称不能为空');
      return;
    }
    setCreating(true);
    setFormError('');
    try {
      const created = await api.createDataPlaneNode(name);
      await api.setInstanceNode(props.instance.instanceId, created.id);
      addToast('success', `已创建节点 "${created.name}" 并绑定实例`);
      await props.onChanged();
      props.onClose();
    } catch (err) {
      addToast('error', `新建节点失败: ${(err as Error).message}`);
    } finally {
      setCreating(false);
    }
  };

  const handleOverlayClick = (e: MouseEvent) => {
    if (e.target === e.currentTarget) props.onClose();
  };

  return (
    <div
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      onClick={handleOverlayClick}
    >
      <div class="bg-white dark:bg-dark-100 rounded-xl shadow-2xl w-full max-w-sm max-h-[80vh] flex flex-col mx-4">
        {/* Header */}
        <div class="flex items-center justify-between px-5 py-4 border-b border-gray-200 dark:border-dark-200">
          <div>
            <h3 class="text-base font-bold">选择接入节点</h3>
            <p class="text-xs text-secondary mt-0.5 truncate max-w-56">{props.instance.name}</p>
          </div>
          <button
            onClick={props.onClose}
            class="text-secondary hover:text-gray-800 dark:hover:text-gray-200 text-xl leading-none"
            title="关闭"
          >
            ✕
          </button>
        </div>

        {/* Options */}
        <div class="flex-1 overflow-y-auto p-2">
          <button
            class={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-left text-sm transition-colors ${
              currentId() === '' ? 'bg-primary/10' : 'hover:bg-gray-50 dark:hover:bg-dark-200'
            }`}
            disabled={busy()}
            onClick={() => bind(null)}
          >
            <span class="flex-1 min-w-0">
              <span class="block font-medium">未绑定</span>
              <span class="block text-xs text-secondary truncate">该实例不接收任何配置</span>
            </span>
            <Show when={currentId() === ''}>
              <FaSolidCheck class="text-primary shrink-0" />
            </Show>
          </button>

          <For each={props.nodes}>
            {(n) => (
              <button
                class={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-left text-sm transition-colors ${
                  currentId() === n.id ? 'bg-primary/10' : 'hover:bg-gray-50 dark:hover:bg-dark-200'
                }`}
                disabled={busy()}
                onClick={() => bind(n.id)}
              >
                <span class="flex-1 min-w-0">
                  <span class="block font-medium truncate inline-flex items-center gap-2">
                    <span class="truncate">{n.name}</span>
                    {n.id === DEFAULT_NODE_ID && <span class="badge badge-warning shrink-0">默认</span>}
                  </span>
                  <span class="block text-xs text-secondary truncate">
                    在线 {n.onlineCount}/{n.instanceCount} · 引用路由 {n.routeCount}
                  </span>
                </span>
                <Show when={currentId() === n.id}>
                  <FaSolidCheck class="text-primary shrink-0" />
                </Show>
              </button>
            )}
          </For>
        </div>

        {/* Create node */}
        <form
          onSubmit={createAndBind}
          class="border-t border-gray-200 dark:border-dark-200 px-5 py-4"
        >
          <label class="text-xs text-secondary block mb-2">新建节点并绑定</label>
          <div class="flex gap-2">
            <input
              class="input py-1.5 text-sm flex-1"
              placeholder="节点名称，例如：home-eu"
              value={newName()}
              onInput={(e) => setNewName(e.currentTarget.value)}
              disabled={busy() || creating()}
            />
            <button
              type="submit"
              class="btn btn-primary shrink-0 inline-flex items-center gap-1 disabled:opacity-50"
              disabled={busy() || creating() || !newName().trim()}
            >
              <FaSolidPlus />
              创建
            </button>
          </div>
          <Show when={formError()}>
            <p class="text-danger text-xs mt-1.5">{formError()}</p>
          </Show>
        </form>
      </div>
    </div>
  );
}
