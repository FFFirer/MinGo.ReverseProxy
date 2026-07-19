import { createSignal } from 'solid-js';

export type ToastType = 'success' | 'error' | 'warning';

export interface ToastItem {
  id: number;
  type: ToastType;
  message: string;
}

let nextId = 0;
const [toasts, setToasts] = createSignal<ToastItem[]>([]);

export { toasts };

export function addToast(type: ToastType, message: string) {
  const id = ++nextId;
  setToasts(prev => [...prev, { id, type, message }]);
  const ms = type === 'error' ? 5000 : type === 'warning' ? 4000 : 3000;
  setTimeout(() => removeToast(id), ms);
}

export function removeToast(id: number) {
  setToasts(prev => prev.filter(t => t.id !== id));
}
