import { createSignal, onCleanup } from 'solid-js';
import { api } from '../api/client';

const POLL_INTERVAL = 60_000; // 60 秒

export function useVersionCheck() {
  const [currentVersion, setCurrentVersion] = createSignal<string>('');
  const [newVersion, setNewVersion] = createSignal<string>('');

  let timer: ReturnType<typeof setInterval> | undefined;

  const checkVersion = async () => {
    try {
      const data = await api.get<{ version: string }>('/version');
      const serverVersion = data?.version ?? '';
      if (!currentVersion()) {
        // 首次获取，仅记录
        setCurrentVersion(serverVersion);
      } else if (serverVersion && serverVersion !== currentVersion()) {
        setNewVersion(serverVersion);
        // 发现新版本后停止轮询
        if (timer) clearInterval(timer);
      }
    } catch {
      // 静默失败
    }
  };

  // 立即检查一次
  checkVersion();
  timer = setInterval(checkVersion, POLL_INTERVAL);

  onCleanup(() => {
    if (timer) clearInterval(timer);
  });

  const dismiss = () => {
    setNewVersion('');
    // 重新记录当前版本并恢复轮询
    checkVersion().then(() => {
      timer = setInterval(checkVersion, POLL_INTERVAL);
    });
  };

  const reload = () => {
    window.location.reload();
  };

  return { newVersion, dismiss, reload };
}
