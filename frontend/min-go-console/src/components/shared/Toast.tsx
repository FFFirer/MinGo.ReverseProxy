import { For, type Component } from 'solid-js';
import { toasts, removeToast, type ToastType } from '../../store/toast';
import { FaSolidCheckCircle, FaSolidTimesCircle, FaSolidExclamationTriangle, FaSolidTimes } from 'solid-icons/fa';

const typeStyles: Record<ToastType, string> = {
  success: 'bg-success/10 text-success border-success/30',
  error: 'bg-danger/10 text-danger border-danger/30',
  warning: 'bg-warning/10 text-warning border-warning/30',
};

const typeIcons: Record<ToastType, Component> = {
  success: FaSolidCheckCircle,
  error: FaSolidTimesCircle,
  warning: FaSolidExclamationTriangle,
};

export default function Toast() {
  const items = toasts();
  if (items.length === 0) return null;

  return (
    <div class="fixed top-4 right-4 z-[9999] flex flex-col gap-2 max-w-sm w-full pointer-events-none">
      <For each={items}>{(t) => (
        <div
          class={`pointer-events-auto flex items-start gap-3 px-4 py-3 rounded-lg border shadow-lg transition-all duration-300 animate-slide-in ${typeStyles[t.type]}`}
        >
          {(() => { const Icon = typeIcons[t.type]; return <Icon class="mt-0.5" />; })()}
          <span class="flex-1 text-sm font-medium">{t.message}</span>
          <button
            onClick={() => removeToast(t.id)}
            class="text-current opacity-60 hover:opacity-100 transition-opacity"
          >
            <FaSolidTimes />
          </button>
        </div>
      )}</For>
    </div>
  );
}
