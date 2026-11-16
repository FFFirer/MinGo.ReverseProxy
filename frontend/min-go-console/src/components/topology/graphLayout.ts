import type { TopologyEdge, TopologyNode, TopologyNodeKind } from '../../types';

/** 列顺序即流量方向：接入通道 → 路由 → 集群 → 目标端点 */
export const LAYERS: TopologyNodeKind[] = ['connection', 'route', 'cluster', 'destination'];

export const LAYER_TITLES: Record<TopologyNodeKind, string> = {
  connection: '接入通道',
  route: '路由',
  cluster: '集群',
  destination: '目标端点',
};

/** 跳转到对应管理页 */
export const LAYER_LINKS: Record<TopologyNodeKind, string> = {
  connection: '/connections',
  route: '/routes',
  cluster: '/clusters',
  destination: '/clusters',
};

export const NODE_W = 196;
export const NODE_H = 56;

const GAP_X = 92;
const GAP_Y = 20;
const PAD = 20;
const HEADER_H = 28;

export interface NodePosition {
  node: TopologyNode;
  x: number;
  y: number;
}

export interface DrawnEdge {
  source: string;
  target: string;
  kind: string;
  d: string;
}

export interface LayoutResult {
  positions: Map<string, NodePosition>;
  columns: { kind: TopologyNodeKind; title: string; x: number; count: number }[];
  edges: DrawnEdge[];
  width: number;
  height: number;
  /** 列标题基线，由布局内部的内边距推出 */
  headerY: number;
}

export interface Adjacency {
  outgoing: Map<string, string[]>;
  incoming: Map<string, string[]>;
}

export function adjacency(edges: TopologyEdge[]): Adjacency {
  const outgoing = new Map<string, string[]>();
  const incoming = new Map<string, string[]>();
  const push = (map: Map<string, string[]>, key: string, value: string) => {
    const list = map.get(key);
    if (list) list.push(value);
    else map.set(key, [value]);
  };

  for (const edge of edges) {
    push(outgoing, edge.source, edge.target);
    push(incoming, edge.target, edge.source);
  }
  return { outgoing, incoming };
}

/**
 * 从若干起点出发，沿流量方向的上游与下游闭包（含起点自身）。
 * 用于链路高亮和"只看异常"——只留下能解释问题的那条路径，而不是孤立的一个节点。
 */
export function closure(
  seeds: Iterable<string>,
  { outgoing, incoming }: Adjacency,
): Set<string> {
  const found = new Set<string>();
  const walk = (start: string, map: Map<string, string[]>) => {
    const stack = [start];
    while (stack.length) {
      const current = stack.pop() as string;
      for (const next of map.get(current) ?? []) {
        if (found.has(next)) continue;
        found.add(next);
        stack.push(next);
      }
    }
  };

  for (const seed of seeds) {
    found.add(seed);
    walk(seed, outgoing);
    walk(seed, incoming);
  }
  return found;
}

/**
 * 分层布局：节点按列排布，列内用上/下游邻居的序号重心排序两轮（正向 + 反向），
 * 以减少连线交叉。确定性布局比力导向更易读，也无需引入图形库。
 */
export function layoutGraph(nodes: TopologyNode[], edges: TopologyEdge[]): LayoutResult {
  const { outgoing, incoming } = adjacency(edges);
  const columns = LAYERS.map((kind) =>
    nodes.filter((n) => n.kind === kind).sort((a, b) => a.label.localeCompare(b.label)),
  );

  const rank = new Map<string, number>();
  const renumber = (list: TopologyNode[]) => {
    list.forEach((n, i) => rank.set(n.id, i));
    return list;
  };
  columns.forEach(renumber);

  const barycenter = (node: TopologyNode, map: Map<string, string[]>, fallback: number) => {
    const neighbors = map.get(node.id);
    if (!neighbors?.length) return fallback;
    let sum = 0;
    let count = 0;
    for (const id of neighbors) {
      const value = rank.get(id);
      if (value === undefined) continue;
      sum += value;
      count += 1;
    }
    return count ? sum / count : fallback;
  };

  const sweep = (map: Map<string, string[]>, from: number, to: number, step: number) => {
    for (let li = from; li !== to; li += step) {
      const seeded = columns[li].map((node, index) => ({ node, fallback: index }));
      seeded.sort(
        (a, b) =>
          barycenter(a.node, map, a.fallback) - barycenter(b.node, map, b.fallback) ||
          a.node.label.localeCompare(b.node.label),
      );
      columns[li] = renumber(seeded.map((s) => s.node));
    }
  };

  // 正向按上游排，反向按下游微调，两轮足够把大部分交叉消掉
  sweep(incoming, 1, columns.length, 1);
  sweep(outgoing, columns.length - 2, -1, -1);

  const maxRows = Math.max(1, ...columns.map((list) => list.length));
  const contentHeight = maxRows * NODE_H + (maxRows - 1) * GAP_Y;

  const positions = new Map<string, NodePosition>();
  const columnHeaders = columns.map((list, li) => {
    const x = PAD + li * (NODE_W + GAP_X);
    const columnHeight = list.length ? list.length * NODE_H + (list.length - 1) * GAP_Y : 0;
    const top = PAD + HEADER_H + (contentHeight - columnHeight) / 2;
    list.forEach((node, i) => positions.set(node.id, { node, x, y: top + i * (NODE_H + GAP_Y) }));
    return { kind: LAYERS[li], title: LAYER_TITLES[LAYERS[li]], x, count: list.length };
  });

  const drawn: DrawnEdge[] = [];
  for (const edge of edges) {
    const from = positions.get(edge.source);
    const to = positions.get(edge.target);
    if (!from || !to) continue;
    const x1 = from.x + NODE_W;
    const y1 = from.y + NODE_H / 2;
    const x2 = to.x;
    const y2 = to.y + NODE_H / 2;
    const mid = (x1 + x2) / 2;
    drawn.push({ ...edge, d: `M${x1},${y1} C${mid},${y1} ${mid},${y2} ${x2},${y2}` });
  }

  return {
    positions,
    columns: columnHeaders,
    edges: drawn,
    width: PAD * 2 + LAYERS.length * NODE_W + (LAYERS.length - 1) * GAP_X,
    height: PAD * 2 + HEADER_H + contentHeight,
    headerY: PAD + 16,
  };
}
