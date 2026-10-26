import { FaSolidSync, FaSolidTimes } from 'solid-icons/fa';

interface Props {
  version: string;
  onDismiss: () => void;
  onReload: () => void;
}

export default function VersionUpdateNotification(props: Props) {
  return (
    <div class="fixed bottom-4 right-4 z-[9999] pointer-events-none">
      <div class="pointer-events-auto flex items-center gap-3 px-4 py-3 rounded-lg bg-primary/10 text-primary border border-primary/30 shadow-lg animate-slide-in">
        <FaSolidSync class="animate-spin" style="animation-duration: 3s" />
        <span class="text-sm font-medium">
          新版本 v{props.version} 已发布，点击刷新
        </span>
        <button
          onClick={props.onReload}
          class="px-3 py-1 rounded bg-primary text-white text-xs font-medium hover:bg-primary/80 transition-colors"
        >
          刷新
        </button>
        <button
          onClick={props.onDismiss}
          class="text-current opacity-60 hover:opacity-100 transition-opacity"
        >
          <FaSolidTimes />
        </button>
      </div>
    </div>
  );
}
