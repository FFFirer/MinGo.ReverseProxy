import { user, setUser, loading, setLoading } from './user-signal';
import { api } from '../api/client';
import type { UserInfo } from '../types';

export { user, loading, setUser };

export async function fetchUser() {
  setLoading(true);
  try {
    const userData = await api.get<UserInfo>('/auth/me');
    setUser(userData);
  } catch (err) {
    console.error('[fetchUser] failed:', err);
    setUser(null);
  } finally {
    setLoading(false);
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
