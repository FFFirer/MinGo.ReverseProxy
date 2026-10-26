import { createSignal, For, Show, JSX } from 'solid-js';

export interface Column<T> {
  /** 唯一标识 */
  key: string;
  /** 表头文本 */
  title: string;
  /** 移动端是否可见，默认 false */
  mobileVisible?: boolean;
  /** 单元格渲染函数 */
  cell: (row: T) => JSX.Element | string | number;
  /** 移动端详情面板中的标签（默认用 title） */
  mobileLabel?: string;
  /** td/th 额外样式类 */
  class?: string;
}

export interface TableProps<T> {
  /** 列配置 */
  columns: Column<T>[];
  /** 数据源 */
  data: T[];
  /** 行唯一键 */
  rowKey: (row: T) => string | number;
  /** 空数据提示 */
  emptyMessage?: string | JSX.Element;
  /** 是否启用移动端行展开详情，默认 true */
  expandable?: boolean;
  /** 紧凑模式（小 padding），用于嵌入弹窗等场景 */
  compact?: boolean;
}

export default function Table<T>(props: TableProps<T>) {
  const [expandedRow, setExpandedRow] = createSignal<string | number | null>(null);

  const expandable = () => props.expandable !== false;
  const compact = () => props.compact === true;

  const cellClass = () =>
    compact() ? 'py-2 px-3' : 'py-3 px-4';

  const toggleRow = (key: string | number) => {
    setExpandedRow(prev => (prev === key ? null : key));
  };

  const handleRowClick = (e: MouseEvent, key: string | number) => {
    if (!expandable()) return;
    // 点击按钮、链接等交互元素时不触发行展开
    const target = e.target as HTMLElement;
    if (target.closest('button, a, input, select, [role="button"]')) return;
    toggleRow(key);
  };

  return (
    <div class="overflow-x-auto">
      <table class="w-full">
        <thead>
          <tr class="border-b border-gray-200 dark:border-dark-200">
            <For each={props.columns}>
              {(col) => (
                <th
                  class={`text-left ${cellClass()} font-medium text-secondary whitespace-nowrap ${
                    col.mobileVisible ? '' : 'hidden md:table-cell'
                  } ${col.class || ''}`}
                >
                  {col.title}
                </th>
              )}
            </For>
          </tr>
        </thead>
        <tbody>
          <For each={props.data}>
            {(row) => {
              const key = () => props.rowKey(row);
              const isExpanded = () => expandedRow() === key();

              return (
                <>
                  <tr
                    class={`border-b border-gray-100 dark:border-dark-100 hover:bg-gray-50 dark:hover:bg-dark-100/50 ${
                      expandable() ? 'cursor-pointer md:cursor-default' : ''
                    } ${isExpanded() ? 'bg-gray-50 dark:bg-dark-100/30' : ''}`}
                    onClick={(e) => handleRowClick(e, key())}
                  >
                    <For each={props.columns}>
                      {(col) => (
                        <td
                          class={`${cellClass()} whitespace-nowrap ${
                            col.mobileVisible ? '' : 'hidden md:table-cell'
                          } ${col.class || ''}`}
                        >
                          {col.cell(row)}
                        </td>
                      )}
                    </For>
                  </tr>

                  {/* 移动端展开详情行 */}
                  <Show when={expandable() && isExpanded()}>
                    <tr class="md:hidden border-b border-gray-100 dark:border-dark-100">
                      <td
                        colspan={props.columns.length}
                        class="px-4 py-3 bg-gray-50/80 dark:bg-dark-200/40"
                      >
                        <div class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
                          <For each={props.columns}>
                            {(col) => (
                              <div class="flex flex-col gap-0.5">
                                <span class="text-secondary text-xs font-medium">
                                  {col.mobileLabel || col.title}
                                </span>
                                <span class="whitespace-normal break-all">
                                  {col.cell(row)}
                                </span>
                              </div>
                            )}
                          </For>
                        </div>
                      </td>
                    </tr>
                  </Show>
                </>
              );
            }}
          </For>

          {/* 空数据 */}
          <Show when={props.data.length === 0}>
            <tr>
              <td
                colspan={props.columns.length}
                class={`${cellClass()} text-center text-secondary py-12`}
              >
                {props.emptyMessage || '暂无数据'}
              </td>
            </tr>
          </Show>
        </tbody>
      </table>
    </div>
  );
}
