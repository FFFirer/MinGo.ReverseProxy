import { user, setUser, loading, setLoading } from './user-signal';
import { api } from '../api/client';
import type { UserInfo } from '../types';

export { user, loading, setUser };

export async function fetchUser(options?: { silent?: boolean }) {
  if (!options?.silent) setLoading(true);
  try {
    const userData = await api.get<UserInfo>('/auth/me');
    setUser(userData);
  } catch (err) {
    console.error('[fetchUser] failed:', err);
    // 静默刷新失败（如网络抖动）时保留既有会话；401 已由 api client 统一登出
    if (!options?.silent) setUser(null);
  } finally {
    if (!options?.silent) setLoading(false);
  }
}

export async function login(email: string, password: string): Promise<{ success: boolean; message: string }> {
  const res = await fetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ email, password }),
  });
  const data = await res.json().catch(() => ({}));
  if (res.ok) {
    await fetchUser();
    return { success: true, message: data.message || '登录成功' };
  }
  return { success: false, message: data.message || '登录失败，请检查邮箱和密码' };
}

export async function logout() {
  await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' });
  setUser(null);
}
