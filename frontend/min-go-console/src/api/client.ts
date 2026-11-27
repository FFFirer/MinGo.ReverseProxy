import { setUser } from '../store/user-signal';

const API_BASE = '/api';

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const hasBody = options?.body != null;
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    ...options,
    headers: {
      ...(options?.headers || {}),
      ...(hasBody ? { 'Content-Type': 'application/json' } : {}),
    },
  });

  if (res.status === 401) {
    setUser(null);
    throw new Error('Unauthorized');
  }

  if (!res.ok) {
    const text = await res.text();
    let message = text;
    const contentType = res.headers.get('content-type') || '';
    // 兼容 application/json 与 application/problem+json
    if (contentType.includes('json')) {
      try {
        const payload = JSON.parse(text) as Record<string, unknown>;
        const detail = payload.detail ?? payload.message ?? payload.title;
        if (typeof detail === 'string' && detail.trim()) message = detail;
      } catch {
        // 非 JSON 错误体，保留原文
      }
    }
    throw new Error(message || `HTTP ${res.status}`);
  }

  if (res.status === 204 || res.headers.get('content-length') === '0') return undefined as T;

  const contentType = res.headers.get('content-type') || '';
  if (!contentType.includes('json')) {
    // 典型场景：后端路由变更后，旧前端请求落到 SPA 回退拿到 HTML
    throw new Error(
      `服务返回了非 JSON 响应（HTTP ${res.status}），请确认控制面与前端版本一致`,
    );
  }

  return res.json();
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'POST', body: body ? JSON.stringify(body) : undefined }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body ? JSON.stringify(body) : undefined }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
  getTransformSchemas: () =>
    request<import('../types').TransformSchema[]>('/transforms/schemas'),

  // Portainer 实例 CRUD
  getPortainerInstances: () =>
    request<import('../types').PortainerInstance[]>('/portainer/instances'),
  getPortainerInstance: (id: string) =>
    request<import('../types').PortainerInstance>(`/portainer/instances/${encodeURIComponent(id)}`),
  createPortainerInstance: (data: import('../types').PortainerInstance) =>
    request<import('../types').PortainerInstance>('/portainer/instances', {
      method: 'POST',
      body: JSON.stringify(data),
    }),
  updatePortainerInstance: (id: string, data: import('../types').PortainerInstance) =>
    request<import('../types').PortainerInstance>(
      `/portainer/instances/${encodeURIComponent(id)}`,
      { method: 'PUT', body: JSON.stringify(data) },
    ),
  deletePortainerInstance: (id: string) =>
    request<void>(`/portainer/instances/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    }),
  testPortainerInstance: (id: string) =>
    request<import('../types').PortainerStatus>(
      `/portainer/instances/${encodeURIComponent(id)}/test`,
      { method: 'POST' },
    ),

  // Portainer 发现（按实例）
  getPortainerStatus: (instanceId: string) =>
    request<import('../types').PortainerStatus>(`/portainer/${encodeURIComponent(instanceId)}/status`),
  getPortainerDiscovery: (instanceId: string) =>
    request<import('../types').PortainerDiscovery>(`/portainer/${encodeURIComponent(instanceId)}/discovery`),

  // Cloudflare 接入通道
  getConnections: () => request<import('../types').ConnectionSummary[]>('/connections'),
  getTopology: () => request<import('../types').TopologyResponse>('/topology'),

  // 数据面节点（接入通道资产，与实例管理页双向维护）
  getDataPlaneNodes: () => request<import('../types').DataPlaneNode[]>('/data-plane-nodes'),
  createDataPlaneNode: (name: string) =>
    request<import('../types').DataPlaneNode>('/data-plane-nodes', {
      method: 'POST',
      body: JSON.stringify({ name }),
    }),
  renameDataPlaneNode: (id: string, name: string) =>
    request<import('../types').DataPlaneNode>(`/data-plane-nodes/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify({ name }),
    }),
  deleteDataPlaneNode: (id: string) =>
    request<void>(`/data-plane-nodes/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  // 实例节点绑定（nodeId 为 null 解绑）
  setInstanceNode: (instanceId: string, nodeId: string | null) =>
    request<import('../types').GatewayInstance>(`/instances/${encodeURIComponent(instanceId)}/node`, {
      method: 'PUT',
      body: JSON.stringify({ nodeId }),
    }),

  getCloudflareSettings: () =>
    request<import('../types').CloudflareSettings>('/cloudflare/settings'),
  saveCloudflareSettings: (data: { apiToken: string; accountId: string; timeoutSeconds: number }) =>
    request<import('../types').CloudflareSettings>('/cloudflare/settings', {
      method: 'PUT',
      body: JSON.stringify(data),
    }),
  getCloudflareStatus: () => request<import('../types').CloudflareStatus>('/cloudflare/status'),
  getCloudflareZones: () => request<import('../types').CloudflareZone[]>('/cloudflare/zones'),
  getCloudflareTunnels: () => request<import('../types').CloudflareTunnel[]>('/cloudflare/tunnels'),
  createCloudflareTunnel: (name: string) =>
    request<import('../types').CloudflareTunnel>('/cloudflare/tunnels', {
      method: 'POST',
      body: JSON.stringify({ name }),
    }),
  updateCloudflareTunnel: (id: string, name: string) =>
    request<import('../types').CloudflareTunnel>(`/cloudflare/tunnels/${encodeURIComponent(id)}`, {
      method: 'PATCH',
      body: JSON.stringify({ name }),
    }),
  deleteCloudflareTunnel: (id: string) =>
    request<void>(`/cloudflare/tunnels/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  getTunnelConnections: (id: string) =>
    request<import('../types').TunnelConnection[]>(
      `/cloudflare/tunnels/${encodeURIComponent(id)}/connections`,
    ),
  getTunnelIngress: (id: string) =>
    request<import('../types').TunnelIngressView>(`/cloudflare/tunnels/${encodeURIComponent(id)}/ingress`),
  reconcileTunnel: (id: string) =>
    request<import('../types').ReconcileReport>(`/cloudflare/tunnels/${encodeURIComponent(id)}/reconcile`, {
      method: 'POST',
    }),

  // 证书自动接入（MinGo.CertManager）
  getCertManagerSettings: () =>
    request<import('../types').CertManagerSettings>('/cert-manager/settings'),
  saveCertManagerSettings: (data: { baseUrl: string; apiKey: string; enabled: boolean; timeoutSeconds: number }) =>
    request<import('../types').CertManagerSettings>('/cert-manager/settings', {
      method: 'PUT',
      body: JSON.stringify(data),
    }),
  testCertManagerConnection: () =>
    request<import('../types').CertManagerConnectionResult>('/cert-manager/test', { method: 'POST' }),
  getCertEnrollments: () =>
    request<import('../types').CertificateEnrollment[]>('/cert-enrollments'),
  registerCertEnrollment: (data: { domain: string; isWildcard: boolean; useStaging: boolean }) =>
    request<import('../types').CertificateEnrollment>('/cert-enrollments', {
      method: 'POST',
      body: JSON.stringify(data),
    }),
  syncCertEnrollment: (id: string) =>
    request<void>(`/cert-enrollments/${encodeURIComponent(id)}/sync`, { method: 'POST' }),
  deleteCertEnrollment: (id: string, deleteCertificates = false) =>
    request<void>(
      `/cert-enrollments/${encodeURIComponent(id)}${deleteCertificates ? '?deleteCertificates=true' : ''}`,
      { method: 'DELETE' },
    ),
};
