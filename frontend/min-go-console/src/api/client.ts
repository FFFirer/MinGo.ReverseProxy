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
  getPortainerStatus: () =>
    request<import('../types').PortainerStatus>('/portainer/status'),
  getPortainerDiscovery: () =>
    request<import('../types').PortainerDiscovery>('/portainer/discovery'),
};
