import { For, Show, createEffect, createMemo, createSignal, onCleanup } from 'solid-js';
import { FaSolidPlus, FaSolidMinus, FaSolidMaximize } from 'solid-icons/fa';
import type { TopologyEdge, TopologyNode, TopologyNodeStatus } from '../../types';
import { NODE_H, NODE_W, adjacency, closure, layoutGraph } from './graphLayout';

interface Props {
  nodes: TopologyNode[];
  edges: TopologyEdge[];
  selectedId: string | null;
  onSelect: (id: string | null) => void;
}

/**
 * Tailwind v4 只生成源码里出现过的类名，运行时拼接的动态类不生效，
 * 因此状态样式全部写成字面量查表。
 */
const RECT_STYLES: Record<TopologyNodeStatus, string> = {
  ok: 'fill-white dark:fill-dark-100 stroke-gray-300 dark:stroke-dark-200',
  warning: 'fill-white dark:fill-dark-100 stroke-warning',
  danger: 'fill-white dark:fill-dark-100 stroke-danger',
  disabled: 'fill-gray-50 dark:fill-dark-200 stroke-gray-200 dark:stroke-dark-300',
};

const DOT_STYLES: Record<TopologyNodeStatus, string> = {
  ok: 'fill-success',
  warning: 'fill-warning',
  danger: 'fill-danger',
  disabled: 'fill-gray-400',
};

const DOT_BG: Record<TopologyNodeStatus, string> = {
  ok: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-danger',
  disabled: 'bg-gray-400',
};

const LEGEND: { status: TopologyNodeStatus; label: string }[] = [
  { status: 'ok', label: '链路完整' },
  { status: 'warning', label: '需关注' },
  { status: 'danger', label: '会失败' },
  { status: 'disabled', label: '已停用' },
];

const clip = (text: string, max: number) => (text.length > max ? `${text.slice(0, max - 1)}…` : text);

export default function TopologyGraph(props: Props) {
  const [hovered, setHovered] = createSignal<string | null>(null);
  const [zoom, setZoom] = createSignal(1);
  const [manualZoom, setManualZoom] = createSignal(false);
  let scrollEl: HTMLDivElement | undefined;

  const layout = createMemo(() => layoutGraph(props.nodes, props.edges));
  const links = createMemo(() => adjacency(props.edges));

  // 悬停优先于选中：没有选中时也能预览链路
  const focus = createMemo(() => {
    const id = hovered() ?? props.selectedId;
    if (!id || !layout().positions.has(id)) return null;
    return closure([id], links());
  });

  const isVisible = (id: string) => {
    const set = focus();
    return !set || set.has(id);
  };

  const fit = () => {
    const available = scrollEl?.clientWidth ?? 0;
    if (!available) return;
    setZoom(Math.min(1, Math.max(0.35, (available - 24) / layout().width)));
  };

  const stepZoom = (delta: number) => {
    setManualZoom(true);
    setZoom((value) => Math.min(1.6, Math.max(0.35, Number((value + delta).toFixed(2)))));
  };

  const onResize = () => {
    if (!manualZoom()) fit();
  };
  window.addEventListener('resize', onResize);
  onCleanup(() => window.removeEventListener('resize', onResize));

  // 数据或筛选变化后重新适配宽度，手动缩放除外
  createEffect(() => {
    layout().width;
    if (!manualZoom()) fit();
  });

  return (
    <div class="flex flex-col gap-2">
      <div class="flex items-center justify-between flex-wrap gap-2">
        <div class="flex items-center gap-4 text-xs text-secondary flex-wrap">
          <For each={LEGEND}>
            {(item) => (
              <span class="flex items-center gap-1.5">
                <span class={`inline-block w-2.5 h-2.5 rounded-full ${DOT_BG[item.status]}`} />
                {item.label}
              </span>
            )}
          </For>
          <span class="hidden sm:inline text-gray-400">悬停预览链路，单击节点锁定详情</span>
        </div>
        <div class="flex items-center gap-1">
          <button class="btn-icon btn-icon-primary" title="缩小" onClick={() => stepZoom(-0.15)}>
            <FaSolidMinus />
          </button>
          <span class="text-xs text-secondary w-12 text-center tabular-nums">
            {Math.round(zoom() * 100)}%
          </span>
          <button class="btn-icon btn-icon-primary" title="放大" onClick={() => stepZoom(0.15)}>
            <FaSolidPlus />
          </button>
          <button
            class="btn-text btn-text-primary"
            onClick={() => {
              setManualZoom(false);
              fit();
            }}
          >
            <FaSolidMaximize class="w-3.5 mr-1" />
            适应宽度
          </button>
        </div>
      </div>

      <div
        ref={scrollEl}
        class="overflow-auto rounded-lg border border-gray-200 dark:border-dark-200 bg-gray-50 dark:bg-dark-200"
      >
        <Show
          when={props.nodes.length}
          fallback={
            <div class="p-10 text-center text-sm text-secondary">
              没有匹配的节点，试试清空搜索或关闭"只看异常"。
            </div>
          }
        >
          <svg
            width={layout().width * zoom()}
            height={layout().height * zoom()}
            viewBox={`0 0 ${layout().width} ${layout().height}`}
            class="block"
            onClick={(event) => {
              if (event.target === event.currentTarget) props.onSelect(null);
            }}
          >
            <For each={layout().columns}>
              {(column) => (
                <text
                  x={column.x}
                  y={layout().headerY}
                  class="text-[11px] font-semibold fill-gray-500 dark:fill-gray-400"
                >
                  {`${column.title} · ${column.count}`}
                </text>
              )}
            </For>

            <g>
              <For each={layout().edges}>
                {(edge) => {
                  const active = () => focus() !== null && isVisible(edge.source) && isVisible(edge.target);
                  return (
                    <path
                      d={edge.d}
                      class={`fill-none transition-all duration-150 ${
                        active()
                          ? 'stroke-primary opacity-100'
                          : focus() === null
                            ? 'stroke-gray-300 dark:stroke-dark-100 opacity-80'
                            : 'stroke-gray-300 dark:stroke-dark-100 opacity-15'
                      }`}
                      stroke-width={active() ? 2 : 1.25}
                    />
                  );
                }}
              </For>
            </g>

            <g>
              <For each={props.nodes}>
                {(node) => (
                  <Show when={layout().positions.get(node.id)}>
                    {(position) => (
                      <g
                        transform={`translate(${position().x},${position().y})`}
                        class={`cursor-pointer transition-opacity duration-150 ${
                          isVisible(node.id) ? 'opacity-100' : 'opacity-20'
                        }`}
                        onMouseEnter={() => setHovered(node.id)}
                        onMouseLeave={() => setHovered(null)}
                        onClick={(event) => {
                          event.stopPropagation();
                          props.onSelect(props.selectedId === node.id ? null : node.id);
                        }}
                      >
                        <Show when={props.selectedId === node.id}>
                          <rect
                            x={-4}
                            y={-4}
                            width={NODE_W + 8}
                            height={NODE_H + 8}
                            rx={12}
                            class="fill-none stroke-primary"
                            stroke-width={2}
                          />
                        </Show>
                        <rect
                          width={NODE_W}
                          height={NODE_H}
                          rx={10}
                          class={RECT_STYLES[node.status]}
                          stroke-width={1.5}
                        />
                        <text x={12} y={23} class="text-[12px] font-medium fill-gray-800 dark:fill-gray-100">
                          {clip(node.label, 20)}
                        </text>
                        <Show when={node.subLabel}>
                          <text x={12} y={41} class="text-[10px] fill-gray-500 dark:fill-gray-400">
                            {clip(node.subLabel ?? '', 28)}
                          </text>
                        </Show>
                        <circle cx={NODE_W - 14} cy={NODE_H / 2} r={5} class={DOT_STYLES[node.status]} />
                      </g>
                    )}
                  </Show>
                )}
              </For>
            </g>
          </svg>
        </Show>
      </div>
    </div>
  );
}
